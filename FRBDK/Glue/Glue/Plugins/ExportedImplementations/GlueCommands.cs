﻿using System.Diagnostics;
using FlatRedBall.Glue.Plugins.ExportedImplementations.CommandInterfaces;
using FlatRedBall.Glue.Plugins.ExportedInterfaces;
using FlatRedBall.Glue.Plugins.ExportedInterfaces.CommandInterfaces;
using Glue;
using System;

namespace FlatRedBall.Glue.Plugins.ExportedImplementations
{
    public class GlueCommands : IGlueCommands
    {
        static GlueCommands mSelf;
        public static GlueCommands Self
        {
            get
            {
                if (mSelf == null)
                {
                    mSelf = new GlueCommands();
                }
                return mSelf;
            }
        }

        public void DoOnUiThread(Action action)
        {
            MainGlueWindow.Self.Invoke(action);
        }

        public void CloseGlue()
        {
            MainGlueWindow.Self.Close();
            Process.GetCurrentProcess().Kill();
        }

        public GlueCommands()
        {
            mSelf = this;
            GenerateCodeCommands = new GenerateCodeCommands();
            GluxCommands = new GluxCommands();
            OpenCommands = new OpenCommands();
            ProjectCommands = new ProjectCommands();
            RefreshCommands = new RefreshCommands();
            TreeNodeCommands = new TreeNodeCommands();
            UpdateCommands = new UpdateCommands();
            DialogCommands = new DialogCommands();
            GlueViewCommands = new GlueViewCommands();
            FileCommands = new FileCommands();
            SelectCommands = new SelectCommands();
        }

        public IGenerateCodeCommands GenerateCodeCommands{ get; private set; }

        public IGluxCommands GluxCommands { get; private set; }

        public IOpenCommands OpenCommands { get; private set; }

        public IProjectCommands ProjectCommands { get; private set; }

        public IRefreshCommands RefreshCommands { get; private set; }

        public ITreeNodeCommands TreeNodeCommands { get; private set; }

        public IUpdateCommands UpdateCommands { get; private set; }

        public IDialogCommands DialogCommands { get; private set; }

        public GlueViewCommands GlueViewCommands { get; private set; }

        public IFileCommands FileCommands { get; private set; }

        public ISelectCommands SelectCommands { get; private set; }

        public string GetAbsoluteFileName(SaveClasses.ReferencedFileSave rfs)
        {
            if(rfs == null)
            {
                throw new ArgumentNullException("rfs", "The argument ReferencedFileSave should not be null");
            }
            return ProjectManager.MakeAbsolute(rfs.Name, true);
        }

        public string GetAbsoluteFileName(string relativeFileName, bool isContent)
        {
            return ProjectManager.MakeAbsolute(relativeFileName, isContent);
        }
    }
}
