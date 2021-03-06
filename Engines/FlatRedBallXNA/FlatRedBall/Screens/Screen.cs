#if ANDROID || IOS
#define REQUIRES_PRIMARY_THREAD_LOADING
#endif

#region Using

using System;
using System.Collections.Generic;
using System.Reflection;
using FlatRedBall.Math;
using FlatRedBall.Gui;
using FlatRedBall.Instructions;
using FlatRedBall.ManagedSpriteGroups;
using FlatRedBall.Graphics;
using FlatRedBall.Utilities;
using System.Threading;
using FlatRedBall.Input;
using FlatRedBall.IO;

#if !SILVERLIGHT
#endif

#endregion

// Test

namespace FlatRedBall.Screens
{
    #region Enums

    public enum AsyncLoadingState
    {
        NotStarted,
        LoadingScreen,
        Done
    }

    #endregion

    public class Screen : IInstructable
    {
        #region Fields

        int mNumberOfThreadsBeforeAsync = -1;

        protected bool IsPaused = false;

        protected double mTimeScreenWasCreated;
        protected double mAccumulatedPausedTime = 0;

        protected Layer mLayer;

        private string mContentManagerName;

        protected Scene mLastLoadedScene;
        private bool mIsActivityFinished;
        private string mNextScreen;

        private bool mManageSpriteGrids;

        internal Screen mNextScreenToLoadAsync;

#if !FRB_MDX
        Action ActivatingAction;
        Action DeactivatingAction;
#endif

        #endregion

        #region Properties

        /// <summary>
        /// The list of instructions owned 
        /// by this screen.
        /// </summary>
        /// <remarks>
        /// These instructions will be automatically
        /// executed based off of time.  Execution of
        /// these instructions is automatically handled 
        /// by the InstructionManager.
        /// </remarks>
        public InstructionList Instructions
        {
            get;
            private set;
        } 

        public bool ShouldRemoveLayer
        {
            get;
            set;
        }

        /// <summary>
        /// Returns how much time the Screen has spent paused
        /// </summary>
        public double AccumulatedPauseTime
        {
            get
            {
                // Internally we just use the accumulated pause time
                // without considering when the Screen was started.  But
                // for external reporting we need to report just the actual
                // paused time.
                return mAccumulatedPausedTime - mTimeScreenWasCreated;
            }
        }

        /// <summary>
        /// Returns the number of seconds since the Screen was initialized, excluding paused time.
        /// This value begins at 0 when the Screen is first created.
        /// </summary>
        /// <remarks>
        /// If a screen is never paused, then its PauseAdjustedCurrentTime value increases regularly.
        /// If a screen is never paused and it is the first screen in the game, then PauseAdjustedCurrentTime
        /// will equal TimeManager.CurrentTime.
        /// </remarks>
        /// <seealso cref="AccumulatedPauseTime"/>
        public double PauseAdjustedCurrentTime
        {
            get { return TimeManager.CurrentTime - mAccumulatedPausedTime; }
        }


        public Action ScreenDestroy { get; set; }

        public int ActivityCallCount
        {
            get;
            set;
        }

        public string ContentManagerName
        {
            get { return mContentManagerName; }
        }

        #region XML Docs
        /// <summary>
        /// Gets and sets whether the activity is finished for a particular screen.
        /// </summary>
        /// <remarks>
        /// If activity is finished, then the ScreenManager or parent
        /// screen (if the screen is a popup) knows to destroy the screen
        /// and loads the NextScreen class.</remarks>
        #endregion
        public bool IsActivityFinished
        {
            get { return mIsActivityFinished; }
            set 
			{ 
				mIsActivityFinished = value; 
			}

        }

        public AsyncLoadingState AsyncLoadingState
        {
            get;
            private set;
        }

        public Layer Layer
        {
            get { return mLayer; }
            set { mLayer = value; }
        }

        public bool ManageSpriteGrids
        {
            get { return mManageSpriteGrids; }
            set { mManageSpriteGrids = value; }
        }

        #region XML Docs
        /// <summary>
        /// The fully qualified path of the Screen-inheriting class that this screen is 
        /// to link to.
        /// </summary>
        /// <remarks>
        /// This property is read by the ScreenManager when IsActivityFinished is
        /// set to true.  Therefore, this must always be set to some value before
        /// or in the same frame as when IsActivityFinished is set to true.
        /// </remarks>
        #endregion
        public string NextScreen
        {
            get { return mNextScreen; }
            set { mNextScreen = value; }
        }

        public bool IsMovingBack { get; set; }

        protected bool UnloadsContentManagerWhenDestroyed
        {
            get;
            set;
        }

        public bool HasDrawBeenCalled
        {
            get;
            internal set;
        }

        #endregion

        #region Methods

        #region Constructor

        public Screen(string contentManagerName)
        {
            this.Instructions = new InstructionList();
            ShouldRemoveLayer = true;
            UnloadsContentManagerWhenDestroyed = true;
            mContentManagerName = contentManagerName;
            mManageSpriteGrids = true;
            IsActivityFinished = false;

            mLayer = ScreenManager.NextScreenLayer;

            ActivatingAction = new Action(Activating);
            DeactivatingAction = new Action(OnDeactivating);

            StateManager.Current.Activating += ActivatingAction;
            StateManager.Current.Deactivating += DeactivatingAction;
			
			if (ScreenManager.ShouldActivateScreen)
            {
                Activating();
            }
        }

        #endregion

        #region Public Methods
        public virtual void Activating()
        {
            this.PreActivate();// for generated code to override, to reload the statestack
            this.OnActivate(PlatformServices.State);// for user created code
        }


        private void OnDeactivating()
        {
            this.PreDeactivate();// for generated code to override, to save the statestack
            this.OnDeactivate(PlatformServices.State);// for user generated code;
        }

        #region Activation Methods
#if !FRB_MDX
        protected virtual void OnActivate(StateManager state)
        {
        }

        protected virtual void PreActivate()
        {
        }

        protected virtual void OnDeactivate(StateManager state)
        {
        }

        protected virtual void PreDeactivate()
        {
        }
#endif		
        #endregion

        public virtual void Activity(bool firstTimeCalled)
        {
            if (IsPaused)
            {
                mAccumulatedPausedTime += TimeManager.SecondDifference;
            }

            // This needs to happen after popup activity
            // in case the Screen creates a popup - we don't
            // want 2 activity calls for one frame.  We also want
            // to make sure that popups have the opportunity to handle
            // back calls so that the base doesn't get it.
            if (PlatformServices.BackStackEnabled && InputManager.BackPressed && !firstTimeCalled)
            {
                this.HandleBackNavigation();
            }
        }

        Type asyncScreenTypeToLoad = null;


		public void StartAsyncLoad(Type type, Action afterLoaded = null)
        {
            StartAsyncLoad(type.FullName, afterLoaded);
        }

        public void StartAsyncLoad(string screenType, Action afterLoaded = null)
        {
            if (AsyncLoadingState == AsyncLoadingState.LoadingScreen)
            {
#if DEBUG
                throw new InvalidOperationException("This Screen is already loading a Screen of type " + asyncScreenTypeToLoad + ".  This is a DEBUG-only exception");
#endif
            }
            else if (AsyncLoadingState == AsyncLoadingState.Done)
            {
#if DEBUG
                throw new InvalidOperationException("This Screen has already loaded a Screen of type " + asyncScreenTypeToLoad + ".  This is a DEBUG-only exception");
#endif
            }
            else
            {
				AsyncLoadingState = AsyncLoadingState.LoadingScreen;
				asyncScreenTypeToLoad = ScreenManager.MainAssembly.GetType(screenType);

				if (asyncScreenTypeToLoad == null)
				{
					throw new Exception("Could not find the type " + screenType);
				}

				#if REQUIRES_PRIMARY_THREAD_LOADING
				// We're going to do the "async" loading on the primary thread
				// since we can't actually do it async.
				PerformAsyncLoad();
                if(afterLoaded != null)
                {
                    afterLoaded();
                }
				#else

                mNumberOfThreadsBeforeAsync = FlatRedBallServices.GetNumberOfThreadsToUse();

                FlatRedBallServices.SetNumberOfThreadsToUse(1);

                Action action;

                if(afterLoaded == null)
                {
                    action = (Action)PerformAsyncLoad;
                }
                else
                {
                    action = () =>
                        {
                            PerformAsyncLoad();

                            // We're going to add this to the instruction manager so it executes on the main thread:
                            InstructionManager.AddSafe(new DelegateInstruction(afterLoaded));
                        };
                }



#if WINDOWS_8 || UWP
                System.Threading.Tasks.Task.Run(action);
#else
                ThreadStart threadStart = new ThreadStart(action);
                Thread thread = new Thread(threadStart);
                thread.Start();
#endif
				#endif
            }
        }

        private void PerformAsyncLoad()
        {
            mNextScreenToLoadAsync = (Screen)Activator.CreateInstance(asyncScreenTypeToLoad, new object[0]);

            // Don't add it to the manager!
            mNextScreenToLoadAsync.Initialize(addToManagers:false);

            AsyncLoadingState = AsyncLoadingState.Done;
        }

        public virtual void Initialize(bool addToManagers)
        {

        }


        public virtual void AddToManagers()
        {	
			// We want to start the timer when we actually add to managers - this is when the activity for the Screen starts
			mAccumulatedPausedTime = TimeManager.CurrentTime;
            mTimeScreenWasCreated = TimeManager.CurrentTime;
        }


        public virtual void Destroy()
        {

#if !FRB_MDX
		    StateManager.Current.Activating -= ActivatingAction;
            StateManager.Current.Deactivating -= DeactivatingAction;
#endif			
            if (mLastLoadedScene != null)
            {
                mLastLoadedScene.Clear();
            }


            FlatRedBall.Debugging.Debugger.DestroyText();

            // It's common for users to forget to add Particle Sprites
            // to the mSprites SpriteList.  This will either create leftover
            // particles when the next screen loads or will throw an assert when
            // the ScreenManager checks if there are any leftover Sprites.  To make
            // things easier we'll just clear the Particle Sprites here.
            bool isPopup = this != ScreenManager.CurrentScreen;
            if (!isPopup)
                SpriteManager.RemoveAllParticleSprites();

            if (UnloadsContentManagerWhenDestroyed && mContentManagerName != FlatRedBallServices.GlobalContentManager)
            {
                FlatRedBallServices.Unload(mContentManagerName);
                FlatRedBallServices.Clean();
            }

            if (ShouldRemoveLayer && mLayer != null)
            {
                SpriteManager.RemoveLayer(mLayer);
            }
            if (IsPaused)
            {
                UnpauseThisScreen();
            }
			
			GuiManager.Cursor.IgnoreNextClick = true;

            if(mNumberOfThreadsBeforeAsync != -1)
            {
                FlatRedBallServices.SetNumberOfThreadsToUse(mNumberOfThreadsBeforeAsync);
            }

            if (ScreenDestroy != null)
                ScreenDestroy();
        }

        protected virtual void PauseThisScreen()
        {
            //base.PauseThisScreen();

            this.IsPaused = true;
            InstructionManager.PauseEngine();

        }

        protected virtual void UnpauseThisScreen()
        {
            InstructionManager.UnpauseEngine();
            this.IsPaused = false;
        }

        /// <summary>
        /// Returns the number of seconds since the argument time for the current screen, considering pausing.
        /// </summary>
        /// <remarks>
        /// Each screen has an internal timer which keeps track of how much time has passed since it has been constructed.
        /// This timer always begins at 0. Therefore, the following code will always tell you how long the screen has been alive:
        /// var timeScreenHasBeenAlive = ScreenInstance.PauseAdjustedSecondsSince(0);
        /// </remarks>
        /// <example>
        /// PauseAdjustedSecondsSince can be used to determine if some timed event has expired. For example:
        /// bool isCooldownFinished = PauseAdjustedSecondsSince(lastAbilityUse) >= CooldownTime;
        /// </example>
        /// <param name="time">The time from which to check how much time has passed.</param>
        /// <returns>How much time has passed since the parameter value.</returns>
        public double PauseAdjustedSecondsSince(double time)
        {
            return PauseAdjustedCurrentTime - time;
        }

        #region XML Docs
        /// <summary>Tells the screen that we are done and wish to move to the
        /// supplied screen</summary>
        /// <param>Fully Qualified Type of the screen to move to</param>
        #endregion
        public void MoveToScreen(string screenClass)
        {
            IsActivityFinished = true;
            NextScreen = screenClass;
        }
		
        public void MoveToScreen(Type screenType)
        {
            IsActivityFinished = true;
            NextScreen = screenType.FullName;
        }

        public void RestartScreen(bool reloadContent)
        {
            if (reloadContent == false)
            {
                UnloadsContentManagerWhenDestroyed = false;
            }
            MoveToScreen(this.GetType());
        }

        #endregion

        #region Protected Methods

        /// <param name="state">This should be a valid enum value of the concrete screen type.</param>
        public virtual void MoveToState(int state)
        {
            // no-op
        }

        /// <summary>Default implementation tells the screen manager to finish this screen's activity and navigate
        /// to the previous screen on the backstack.</summary>
        /// <remarks>Override this method if you want to have custom behavior when the back button is pressed.</remarks>
        protected virtual void HandleBackNavigation()
        {

        }

        #endregion

        #endregion
    }
}
