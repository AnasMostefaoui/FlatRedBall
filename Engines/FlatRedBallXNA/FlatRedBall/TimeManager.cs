
using System;
using System.Collections.Generic;

#if !FRB_MDX
using Microsoft.Xna.Framework;
#endif

using System.Text;

using System.Threading;
using FlatRedBall.IO;



namespace FlatRedBall
{
    #region Enums
    /// <summary>
    /// Represents the unit of time measurement.  This can be used in files that store timing information.
    /// </summary>
    public enum TimeMeasurementUnit
    {
        Undefined,
        Millisecond,
        Second
    }
    #endregion

    #region Struct

    public struct TimedSection
    {
        public string Name;
        public double Time;

        public override string ToString()
        {
            return Name + ": " + Time;
        }
    }

    #endregion
    
    public static class TimeManager
    {
        #region Fields

        static float mSecondDifference;
        static float mLastSecondDifference;
        static float mSecondDifferenceSquaredDividedByTwo;

        // This is accessed so many times it's made a field for performance reasons.
        public static double CurrentTime;
        static double mLastCurrentTime;

        static double mTimeFactor = 1.0f;

        static double mCurrentTimeForTimedSections;

#if SILVERLIGHT
        static long mStartingTicks;
#else
        static System.Diagnostics.Stopwatch stopWatch;
#endif

        static List<double> sections = new List<double>();
        static List<string> sectionLabels = new List<string>();

        static List<double> lastSections = new List<double>();
        static List<string> lastSectionLabels = new List<string>();

        static Dictionary<string, double> mPersistentSections = new Dictionary<string, double>();
        static double mLastPersistentTime;

        static Dictionary<string, double> mSumSections = new Dictionary<string, double>();
        static Dictionary<string, int> mSumSectionHitCount = new Dictionary<string, int>();
        static double mLastSumTime;

        static StringBuilder stringBuilder;

        static bool mTimeSectionsEnabled = true;

        static bool mIsPersistentTiming = false;

#if !FRB_MDX
        static GameTime mLastUpdateGameTime;
#endif

		static TimeMeasurementUnit mTimedSectionReportngUnit = TimeMeasurementUnit.Millisecond;

		static float mMaxFrameTime = 0.5f;

        #endregion

        #region Properties

        public static double LastCurrentTime
        {
            get { return mLastCurrentTime; }
        }

        /// <summary>
        /// The number of seconds (usually a fraction of a second) since
        /// the last frame.  This value can be used for time-based movement.
        /// </summary>
        public static float SecondDifference
        {
            get { return mSecondDifference; }
        }

        public static float LastSecondDifference
        {
            get { return mLastSecondDifference; }
        }

        public static float SecondDifferenceSquaredDividedByTwo
        {
            get { return mSecondDifferenceSquaredDividedByTwo; }
        }

        public static bool TimeSectionsEnabled
        {
            get { return mTimeSectionsEnabled; }
            set { mTimeSectionsEnabled = value; }
        }

        /// <summary>
        /// A multiplier for how fast time runs.  This is 1 by default.  Setting
        /// this value to 2 will make everything run twice as fast.
        /// </summary>
        public static double TimeFactor
        {
            get { return mTimeFactor; }
            set { mTimeFactor = value; }
        }

#if FRB_XNA
        public static GameTime LastUpdateGameTime
        {
            get { return mLastUpdateGameTime; }
        }
#endif

		public static TimeMeasurementUnit TimedSectionReportingUnit
        {
            get { return mTimedSectionReportngUnit; }
            set { mTimedSectionReportngUnit = value; }
        }

		public static float MaxFrameTime
		{
			get { return mMaxFrameTime; }
			set { mMaxFrameTime = value; }
		}

        // This was made a Field for performance reasons
        //public static double CurrentTime
        //{
        //    get { return mCurrentTime; }
        //}

        public static Dictionary<string, double> SumSectionDictionary
        {
            get { return mSumSections; }
        }

        public static double SystemCurrentTime
        {
            get 
            { 
#if SILVERLIGHT
                return TimeSpan.FromTicks(System.DateTime.Now.Ticks - mStartingTicks).TotalSeconds;
#else
                
                return stopWatch.Elapsed.TotalSeconds; 
#endif
            }

        }


        public static int TimedSectionCount
        {
            get { return sections.Count; }
        }

        #endregion

        #region Methods

        public static void CreateXmlSumTimeSectionReport(string fileName)
        {
            List<TimedSection> tempList = GetTimedSectionList();

            FileManager.XmlSerialize<List<TimedSection>>(tempList, fileName);
        }

        public static List<TimedSection> GetTimedSectionList()
        {
            List<TimedSection> tempList = new List<TimedSection>(
                mSumSections.Count);

            foreach (KeyValuePair<string, double> kvp in mSumSections)
            {
                TimedSection timedSection = new TimedSection()
                {
                    Name = kvp.Key,
                    Time = kvp.Value
                };

                tempList.Add(timedSection);
            }
            return tempList;
        }

        /// <summary>
        /// Allows the game component to perform any initialization it needs to before starting
        /// to run.  This is where it can query for any required services and load content.
        /// </summary>
        public static void Initialize()
        {
            stringBuilder = new StringBuilder(200);

#if SILVERLIGHT
            mStartingTicks = System.DateTime.Now.Ticks;
#else
            InitializeStopwatch();

#endif
        }

#if !SILVERLIGHT
        public static void InitializeStopwatch()
        {
            stopWatch = new System.Diagnostics.Stopwatch();
            stopWatch.Start();
        }
#endif

        #region TimeSection code

        public static string GetPersistentTimedSections()
        {

            System.Text.StringBuilder sb = new System.Text.StringBuilder();

            foreach (KeyValuePair<string, double> kvp in mPersistentSections)
            {
                sb.Append(kvp.Key).Append(": ").AppendLine(kvp.Value.ToString());
            }

            mIsPersistentTiming = false;

            return sb.ToString();
        }


        public static string GetSumTimedSections()
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();

            foreach (KeyValuePair<string, double> kvp in mSumSections)
            {
				if (TimedSectionReportingUnit == TimeMeasurementUnit.Millisecond)
				{
					sb.Append(kvp.Key).Append(": ").AppendLine((kvp.Value * 1000.0f).ToString("f2"));
				}
				else
				{
					sb.Append(kvp.Key).Append(": ").AppendLine(kvp.Value.ToString());
				}
            }

            return sb.ToString();
        }


        public static string GetTimedSections(bool showTotal)
        {
            stringBuilder.Remove(0, stringBuilder.Length);

            int largestIndex = -1;
            double longestTime = -1;

            for (int i = 0; i < lastSections.Count; i++)
            {
                if (lastSections[i] > longestTime)
                {
                    longestTime = lastSections[i];
                    largestIndex = i;
                }
            }

            for (int i = 0; i < lastSections.Count; i++)
            {
                if (i == largestIndex)
                {
					if (lastSectionLabels[i] != "")
					{
						if (TimedSectionReportingUnit == TimeMeasurementUnit.Millisecond)
						{
							stringBuilder.Append("-!-" + lastSectionLabels[i]).Append(": ").Append(lastSections[i].ToString("f2")).Append("\n");
						}
						else
						{
							stringBuilder.Append("-!-" + lastSectionLabels[i]).Append(": ").Append(lastSections[i].ToString()).Append("\n");
						}
					}
					else
					{
						if (TimedSectionReportingUnit == TimeMeasurementUnit.Millisecond)
						{
							stringBuilder.Append("-!-" + lastSections[i].ToString("f2")).Append("\n");
						}
						else
						{
							stringBuilder.Append("-!-" + lastSections[i].ToString()).Append("\n");
						}
					}
                }
                else
                {
					if (lastSectionLabels[i] != "")
					{
						if (TimedSectionReportingUnit == TimeMeasurementUnit.Millisecond)
						{
							stringBuilder.Append(lastSectionLabels[i]).Append(": ").Append(lastSections[i].ToString("f2")).Append("\n");
						}
						else
						{
							stringBuilder.Append(lastSectionLabels[i]).Append(": ").Append(lastSections[i].ToString()).Append("\n");
						}
					}
					else
					{
						if (TimedSectionReportingUnit == TimeMeasurementUnit.Millisecond)
						{
							stringBuilder.Append(lastSections[i].ToString("f2")).Append("\n");
						}
						else
						{
							stringBuilder.Append(lastSections[i].ToString()).Append("\n");
						}
					}
                }
            }

            if (showTotal)
			{
#if !SILVERLIGHT
				if (TimedSectionReportingUnit == TimeMeasurementUnit.Millisecond)
				{
					stringBuilder.Append("Total Timed: " + ((TimeManager.SystemCurrentTime - TimeManager.mCurrentTimeForTimedSections) * 1000.0f).ToString("f2"));
				}
				else
				{
					stringBuilder.Append("Total Timed: " + (TimeManager.SystemCurrentTime - TimeManager.mCurrentTimeForTimedSections));
				}
#endif
			}

            return stringBuilder.ToString();

        }


        public static void PersistentTimeSection(string label)
        {
#if !SILVERLIGHT
            if (mIsPersistentTiming)
            {
                double currentTime = SystemCurrentTime;
                if (mPersistentSections.ContainsKey(label))
                {
					if (TimedSectionReportingUnit == TimeMeasurementUnit.Millisecond)
					{
						mPersistentSections[label] = ((currentTime - mLastPersistentTime) * 1000.0f);
					}
					else
					{
						mPersistentSections[label] = currentTime - mLastPersistentTime;
					}
                }
                else
                {
					if (TimedSectionReportingUnit == TimeMeasurementUnit.Millisecond)
					{
						mPersistentSections.Add(label, (currentTime - mLastPersistentTime) * 1000.0f);
					}
					else
					{
						mPersistentSections.Add(label, currentTime - mLastPersistentTime);
					}
                }

                mLastPersistentTime = currentTime;
            }
#endif
        }



        public static void StartPersistentTiming()
        {
            mPersistentSections.Clear();

            mIsPersistentTiming = true;
#if !SILVERLIGHT
            mLastPersistentTime = SystemCurrentTime;
#endif
        }

        /// <summary>
        /// Begins Sum Timing
        /// </summary>
        /// <remarks>
        /// <code>
        /// 
        /// StartSumTiming();
        /// 
        /// foreach(Sprite sprite in someSpriteArray)
        /// {
        ///     SumTimeRefresh();
        ///     PerformSomeFunction(sprite);
        ///     SumTimeSection("PerformSomeFunction time:");
        /// 
        /// 
        ///     SumTimeRefresh();
        ///     PerformSomeOtherFunction(sprite);
        ///     SumTimeSection("PerformSomeOtherFunction time:);
        /// 
        /// }
        /// </code>
        ///
        /// </remarks>
        public static void StartSumTiming()
        {
            mSumSections.Clear();
            mSumSectionHitCount.Clear();
#if !SILVERLIGHT
            mLastSumTime = SystemCurrentTime;
#endif
        }


        public static void SumTimeSection(string label)
        {
#if !SILVERLIGHT
            double currentTime = SystemCurrentTime;
            if (mSumSections.ContainsKey(label))
            {
                mSumSections[label] += currentTime - mLastSumTime;
                //mSumSectionHitCount[label]++;
            }
            else
            {
                mSumSections.Add(label, currentTime - mLastSumTime);
                //mSumSectionHitCount.Add(label, 1);
            }
            mLastSumTime = currentTime;
#endif
        }


        public static void SumTimeRefresh()
        {
#if !SILVERLIGHT
            mLastSumTime = SystemCurrentTime;
#endif
        }

        #region XML Docs
        /// <summary>
        /// Stores an unnamed timed section.
        /// </summary>
        /// <remarks>
        /// A timed section is the amount of time (in seconds) since the last time either Update
        /// or TimeSection has been called.  The sections are reset every time Update is called.
        /// The sections can be retrieved through the GetTimedSections method.
        /// <seealso cref="FRB.TimeManager.GetTimedSection"/>
        /// </remarks>
        #endregion
        public static void TimeSection()
        {
            TimeSection("");
        }


        #region XML Docs
        /// <summary>
        /// Stores an named timed section.
        /// </summary>
        /// <remarks>
        /// A timed section is the amount of time (in seconds) since the last time either Update
        /// or TimeSection has been called.  The sections are reset every time Update is called.
        /// The sections can be retrieved through the GetTimedSections method.
        /// <seealso cref="FRB.TimeManager.GetTimedSection"/>
        /// </remarks>
        /// <param name="label">The label for the timed section.</param>
        #endregion
        public static void TimeSection(string label)
        {
            if (mTimeSectionsEnabled)
            {
#if !SILVERLIGHT && !WINDOWS_PHONE
                Monitor.Enter(sections);
#endif

#if !SILVERLIGHT
                double f = (SystemCurrentTime - mCurrentTimeForTimedSections);
                if (TimedSectionReportingUnit == TimeMeasurementUnit.Millisecond)
                {
                    f *= 1000.0f;
                }

                for (int i = sections.Count - 1; i > -1; i--)
                    f -= sections[i];


                sections.Add(f);
                sectionLabels.Add(label);
#endif

#if !SILVERLIGHT && !WINDOWS_PHONE
                Monitor.Exit(sections);
#endif
            }
        }

        #endregion

        public static double SecondsSince(double absoluteTime)
        {
            return CurrentTime - absoluteTime;
        }

        #region XML Docs
        /// <summary>
        /// Performs every-frame logic to update timing values such as CurrentTime and SecondDifference.  If this method is not called, CurrentTime will not advance.
        /// </summary>
        /// <param name="time">The GameTime value provided by the XNA Game class.</param>
        #endregion
        public static void Update(GameTime time)
        {
            mLastUpdateGameTime = time;

            lastSections.Clear();
            lastSectionLabels.Clear();


            for (int i = sections.Count - 1; i > -1; i--)
            {

                lastSections.Insert(0, sections[i]);
                lastSectionLabels.Insert(0, sectionLabels[i]);
            }

            sections.Clear();
            sectionLabels.Clear();

            mLastSecondDifference = mSecondDifference;
            mLastCurrentTime = CurrentTime;

#if !SILVERLIGHT


            const bool useSystemCurrentTime = false;

            double elapsedTime;

            if (useSystemCurrentTime)
            {
                double systemCurrentTime = SystemCurrentTime;
                elapsedTime = systemCurrentTime - mLastCurrentTime;
                mLastCurrentTime = systemCurrentTime;
                //stop big frame times
                if (elapsedTime > MaxFrameTime)
                {
                    elapsedTime = MaxFrameTime;
                }
            }
            else
            {
                /*
                mSecondDifference = (float)(currentSystemTime - mCurrentTime);
                mCurrentTime = currentSystemTime;
                */

                elapsedTime = time.ElapsedGameTime.TotalSeconds * mTimeFactor;

                //stop big frame times
                if (elapsedTime > MaxFrameTime)
                {
                    elapsedTime = MaxFrameTime;
                }



            }

            mSecondDifference = (float)(elapsedTime);
            CurrentTime += elapsedTime;

            double currentSystemTime = SystemCurrentTime + mSecondDifference;

            mSecondDifferenceSquaredDividedByTwo = (mSecondDifference * mSecondDifference) / 2.0f;
            mCurrentTimeForTimedSections = currentSystemTime;
#else
            mSecondDifference = (float)time.ElapsedGameTime.TotalSeconds;
            mSecondDifferenceSquaredDividedByTwo = (mSecondDifference * mSecondDifference) / 2.0F;

            CurrentTime = time.TotalGameTime.TotalSeconds;
            
#endif
        }

        #endregion
    }
}


