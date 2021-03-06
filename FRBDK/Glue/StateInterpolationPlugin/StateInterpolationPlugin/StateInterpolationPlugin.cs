﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using FlatRedBall.Glue.Plugins;
using System.ComponentModel.Composition;
using System.Windows.Forms;
using FlatRedBall.Glue.Plugins.ExportedImplementations.CommandInterfaces;
using System.IO;
using FlatRedBall.Glue;
using FlatRedBall.IO;
using System.Reflection;
using FlatRedBall.Glue.Parsing;
using FlatRedBall.Glue.SaveClasses;
using FlatRedBall.Glue.FormHelpers.PropertyGrids;
using FlatRedBall.Glue.GuiDisplay;
using FlatRedBall.Glue.VSHelpers;


namespace OfficialPlugins.StateInterpolation
{
    [Export(typeof(PluginBase))]
    public class StateInterpolationPlugin : PluginBase
    {
        #region Fields
        CodeBuildItemAdder mItemAdder;



        public const string VariableName = "HasAdvancedInterpolations";

        //MenuStrip mMenuStrip;
        //ToolStripMenuItem mStateInterpolationEnabledMenuItem;
        StateInterpolationCodeGenerator mCodeGenerator;

        #endregion

        public override string FriendlyName
        {
            get { return "State Interpolation Plugin"; }
        }

        public override Version Version
        {
            get { return new Version(1, 1, 2); }
        }

        public override void StartUp()
        {
            // We need this to happen when the glux is loaded
            //UpdateCodeInProjectPresence();
            mItemAdder = new CodeBuildItemAdder();
            mItemAdder.Add("StateInterpolationPlugin.Back.cs");
            mItemAdder.Add("StateInterpolationPlugin.Bounce.cs");
            mItemAdder.Add("StateInterpolationPlugin.Circular.cs");
            mItemAdder.Add("StateInterpolationPlugin.Cubic.cs");
            mItemAdder.Add("StateInterpolationPlugin.Elastic.cs");
            mItemAdder.Add("StateInterpolationPlugin.Exponential.cs");
            mItemAdder.Add("StateInterpolationPlugin.Linear.cs");
            mItemAdder.Add("StateInterpolationPlugin.Quadratic.cs");
            mItemAdder.Add("StateInterpolationPlugin.Quartic.cs");
            mItemAdder.Add("StateInterpolationPlugin.Quintic.cs");
            mItemAdder.Add("StateInterpolationPlugin.ShakeTweener.cs");
            mItemAdder.Add("StateInterpolationPlugin.Sinusoidal.cs");
            mItemAdder.Add("StateInterpolationPlugin.Tweener.cs");
            mItemAdder.Add("StateInterpolationPlugin.TweenerManager.cs");

            mItemAdder.AddFileBehavior = AddFileBehavior.IfOutOfDate;

            mItemAdder.OutputFolderInProject = "StateInterpolation";

            

            this.AdjustDisplayedEntity += HandleAdjustDisplayedEntity;
            this.AdjustDisplayedScreen += HandleAdjustDisplayedScreen;
            this.ReactToLoadedGlux += HandleGluxLoad;
            mCodeGenerator = new StateInterpolationCodeGenerator();
            CodeWriter.CodeGenerators.Add(mCodeGenerator);

        }

        void HandleGluxLoad()
        {
            UpdateCodeInProjectPresence();
        }

        private void HandleAdjustDisplayedEntity(EntitySave entitySave, EntitySavePropertyGridDisplayer displayer)
        {
            displayer.IncludeCustomPropertyMember(VariableName, typeof(bool));
        }

        private void HandleAdjustDisplayedScreen(ScreenSave screenSave, ScreenSavePropertyGridDisplayer displayer)
        {
            displayer.IncludeCustomPropertyMember(VariableName, typeof(bool));
        }


        private void UpdateCodeInProjectPresence()
        {
            Assembly assembly = Assembly.GetExecutingAssembly();
            mItemAdder.PerformAddAndSave(assembly);
        }


        public override bool ShutDown(FlatRedBall.Glue.Plugins.Interfaces.PluginShutDownReason shutDownReason)
        {
            CodeWriter.CodeGenerators.Remove(mCodeGenerator);

            return true;
        }

    }
}
