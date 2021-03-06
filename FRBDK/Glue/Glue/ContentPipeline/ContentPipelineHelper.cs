﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using FlatRedBall.Glue.SaveClasses;
using FlatRedBall.Glue.VSHelpers;
using EditorObjects.Parsing;
using FlatRedBall.Glue.Elements;
using FlatRedBall.Glue.VSHelpers.Projects;
using FlatRedBall.IO;
using System.IO;
using System.Windows.Forms;
using Glue;
using FlatRedBall.Glue.Plugins.ExportedImplementations.CommandInterfaces;
using FlatRedBall.Glue.Managers;
using FlatRedBall.Glue.Plugins.ExportedImplementations;
using Microsoft.Build.Evaluation;

namespace FlatRedBall.Glue.ContentPipeline
{
    public class ContentPipelineHelper
    {
        public static void SetAllFromFile()
        {
            DialogResult result = MessageBox.Show("Are you sure you want to set all files to load from-file?  You " +
                "should probably close Visual Studio while this is running", "Change all to from-file?",
                MessageBoxButtons.YesNo);

            if (result == DialogResult.Yes)
            {

                List<ReferencedFileSave> allReferencedFiles = ObjectFinder.Self.GetAllReferencedFiles();

                
                FlatRedBall.Glue.Controls.InitializationWindow messageWindow = new Controls.InitializationWindow();
                messageWindow.Show(MainGlueWindow.Self);

                messageWindow.Message = "Switching Files...";
                Application.DoEvents();

                int count = 0;

                foreach (ReferencedFileSave rfs in allReferencedFiles)
                {
                    if (rfs.GetAssetTypeInfo() == null || rfs.GetAssetTypeInfo().MustBeAddedToContentPipeline == false)
                    {
                        bool valueBefore = rfs.UseContentPipeline;

                        rfs.UseContentPipeline = false;
                        if (valueBefore != rfs.UseContentPipeline)
                        {
                            messageWindow.SubMessage = "Converting " + rfs.Name;
                            Application.DoEvents();

                            count++;
                            ReactToUseContentPipelineChange(rfs);
                        }
                    }
                }

                GlueCommands.Self.GenerateCodeCommands.GenerateAllCode();

                MainGlueWindow.Self.PropertyGrid.Refresh();


                ProjectManager.SaveProjects();
                GluxCommands.Self.SaveGlux();

                MessageBox.Show("Done converting " + count + " files to load from file.");
            }
        }

        public static void SetAllFromContentPipeline()
        {
            DialogResult result = MessageBox.Show("Are you sure you want to set all files to load through the content pipeline?  You " +
                "should probably close Visual Studio while this is running", "Change all to use the content pipeline?",
                MessageBoxButtons.YesNo);

            if (result == DialogResult.Yes)
            {

                List<ReferencedFileSave> allReferencedFiles = ObjectFinder.Self.GetAllReferencedFiles();
                int count = 0;
                foreach (ReferencedFileSave rfs in allReferencedFiles)
                {
                    if (rfs.GetAssetTypeInfo() != null && !string.IsNullOrEmpty(rfs.GetAssetTypeInfo().ContentProcessor))
                    {
                        bool valueBefore = rfs.UseContentPipeline;

                        rfs.UseContentPipeline = true;
                        if (valueBefore != rfs.UseContentPipeline)
                        {
                            count++;
                            ReactToUseContentPipelineChange(rfs);
                        }
                    }
                }

                GlueCommands.Self.GenerateCodeCommands.GenerateAllCode();


                MainGlueWindow.Self.PropertyGrid.Refresh();


                ProjectManager.SaveProjects();
                GluxCommands.Self.SaveGlux();

                MessageBox.Show("Done converting " + count + " files to use the content pipeline.");
            }

        }

        public static void ReactToUseContentPipelineChange(ReferencedFileSave rfs)
        {
            
            List<string> filesInModifiedRfs = new List<string>();
            bool shouldRemoveAndAdd = false;


            ProjectBase projectBase = ProjectManager.ContentProject;
            if (projectBase == null)
            {
                projectBase = ProjectManager.ProjectBase;
            }

            bool usesContentPipeline = AddOrRemoveIndividualRfs(rfs, filesInModifiedRfs, ref shouldRemoveAndAdd, projectBase);

            if (shouldRemoveAndAdd)
            {
                List<ReferencedFileSave> rfses = new List<ReferencedFileSave>();
                rfses.Add(rfs);
                AddAndRemoveModifiedRfsFiles(rfses, filesInModifiedRfs, projectBase, usesContentPipeline);
            }
        }

        public static void ReactToUseContentPipelineChange(List<ReferencedFileSave> rfses)
        {

            List<string> filesInModifiedRfs = new List<string>();
            bool shouldRemoveAndAdd = false;


            ProjectBase projectBase = ProjectManager.ContentProject;
            if (projectBase == null)
            {
                projectBase = ProjectManager.ProjectBase;
            }

            bool usesContentPipeline = false;
            
            foreach(ReferencedFileSave rfs in rfses)
            {
                usesContentPipeline |= AddOrRemoveIndividualRfs(rfs, filesInModifiedRfs, ref shouldRemoveAndAdd, projectBase);
            }

            if (shouldRemoveAndAdd)
            {

                AddAndRemoveModifiedRfsFiles(rfses, filesInModifiedRfs, projectBase, usesContentPipeline);
            }
        }

        private static void AddAndRemoveModifiedRfsFiles(List<ReferencedFileSave> rfses, List<string> filesInModifiedRfs, ProjectBase projectBase, bool usesContentPipeline)
        {
            #region If the modified file has files it references, we may need to add or remove them.  Do that here

            if (filesInModifiedRfs.Count != 0)
            {

                for (int i = 0; i < filesInModifiedRfs.Count; i++)
                {
                    filesInModifiedRfs[i] = ProjectManager.MakeRelativeContent(filesInModifiedRfs[i]).ToLower();
                }

                List<ReferencedFileSave> allReferencedFiles = ObjectFinder.Self.GetAllReferencedFiles();
                foreach (ReferencedFileSave rfsToRemove in rfses)
                {
                    allReferencedFiles.Remove(rfsToRemove);
                }

                RemoveFilesFromListReferencedByRfses(filesInModifiedRfs, allReferencedFiles);

                #region Loop through all files to add/remove

                foreach (string fileToAddOrRemove in filesInModifiedRfs)
                {

                    List<ProjectBase> projectsAlreadyModified = new List<ProjectBase>();

                    // There are files referenced by the RFS that aren't referenced by others

                    // If moving to content pipeline, remove the files from the project.
                    // If moving to copy if newer, add the files back to the project.
                    string absoluteFileName = ProjectManager.MakeAbsolute(fileToAddOrRemove, true);

                    projectsAlreadyModified.Add(projectBase);

                    #region Uses the content pipeline, remove the file from all projects
                    if (usesContentPipeline)
                    {
                        // Remove this file - it'll automatically be handled by the content pipeline
                        projectBase.RemoveItem(absoluteFileName);

                        foreach (ProjectBase syncedProject in ProjectManager.SyncedProjects)
                        {

                            ProjectBase syncedContentProjectBase = syncedProject;
                            if (syncedProject.ContentProject != null)
                            {
                                syncedContentProjectBase = syncedProject.ContentProject;
                            }

                            if (!projectsAlreadyModified.Contains(syncedContentProjectBase))
                            {
                                projectsAlreadyModified.Add(syncedContentProjectBase);

                                syncedContentProjectBase.RemoveItem(absoluteFileName);
                            }
                        }
                    }
                    #endregion

                    #region Does not use the content pipeline - add the file if necessary

                    else
                    {

                        // This file may have alraedy been part of the project for whatever reason, so we
                        // want to make sure it's not already part of it when we try to add it
                        if (!projectBase.IsFilePartOfProject(absoluteFileName, BuildItemMembershipType.CopyIfNewer))
                        {
                            projectBase.AddContentBuildItem(absoluteFileName, SyncedProjectRelativeType.Contained, false);
                        }
                        foreach (ProjectBase syncedProject in ProjectManager.SyncedProjects)
                        {
                            ProjectBase syncedContentProjectBase = syncedProject;
                            if (syncedProject.ContentProject != null)
                            {
                                syncedContentProjectBase = syncedProject.ContentProject;
                            }

                            if (!projectsAlreadyModified.Contains(syncedContentProjectBase))
                            {
                                projectsAlreadyModified.Add(syncedContentProjectBase);

                                if (syncedContentProjectBase.SaveAsAbsoluteSyncedProject)
                                {
                                    syncedContentProjectBase.AddContentBuildItem(absoluteFileName, SyncedProjectRelativeType.Contained, false);
                                }
                                else
                                {
                                    if (!projectBase.IsFilePartOfProject(absoluteFileName, BuildItemMembershipType.CopyIfNewer))
                                    {
                                        syncedContentProjectBase.AddContentBuildItem(absoluteFileName, SyncedProjectRelativeType.Linked, false);
                                    }
                                }
                            }
                        }
                    }

                    #endregion
                }
                #endregion
            }

            #endregion

            #region Save the synced projects

            foreach (ProjectBase syncedProject in ProjectManager.SyncedProjects)
            {

                ProjectBase syncedContentProjectBase = syncedProject;
                if (syncedProject.ContentProject != null)
                {
                    syncedContentProjectBase = syncedProject.ContentProject;
                }

                syncedContentProjectBase.Save();
            }

            #endregion
        }

        private static bool AddOrRemoveIndividualRfs(ReferencedFileSave rfs, List<string> filesInModifiedRfs, ref bool shouldRemoveAndAdd, ProjectBase projectBase)
        {

            List<ProjectBase> projectsAlreadyModified = new List<ProjectBase>();

            bool usesContentPipeline = rfs.UseContentPipeline || rfs.GetAssetTypeInfo() != null && rfs.GetAssetTypeInfo().MustBeAddedToContentPipeline;

            if (rfs.GetAssetTypeInfo() != null && rfs.GetAssetTypeInfo().MustBeAddedToContentPipeline && rfs.UseContentPipeline == false)
            {
                rfs.UseContentPipeline = true;
                MessageBox.Show("The file " + rfs.Name + " must use the content pipeline");

            }
            else
            {
                string absoluteName = ProjectManager.MakeAbsolute(rfs.Name, true);

                shouldRemoveAndAdd = usesContentPipeline && !projectBase.IsFilePartOfProject(absoluteName, BuildItemMembershipType.CompileOrContentPipeline) ||
                    !usesContentPipeline && !projectBase.IsFilePartOfProject(absoluteName, BuildItemMembershipType.CopyIfNewer);

                if (shouldRemoveAndAdd)
                {
                    projectBase.RemoveItem(absoluteName);
                    projectBase.AddContentBuildItem(absoluteName, SyncedProjectRelativeType.Contained, usesContentPipeline);
                    projectsAlreadyModified.Add(projectBase);

                    #region Loop through all synced projects and add or remove the file referenced by the RFS

                    foreach (ProjectBase syncedProject in ProjectManager.SyncedProjects)
                    {

                        ProjectBase syncedContentProjectBase = syncedProject;
                        if (syncedProject.ContentProject != null)
                        {
                            syncedContentProjectBase = syncedProject.ContentProject;
                        }

                        if (!projectsAlreadyModified.Contains(syncedContentProjectBase))
                        {
                            projectsAlreadyModified.Add(syncedContentProjectBase);
                            syncedContentProjectBase.RemoveItem(absoluteName);

                            if (syncedContentProjectBase.SaveAsAbsoluteSyncedProject)
                            {
                                syncedContentProjectBase.AddContentBuildItem(absoluteName, SyncedProjectRelativeType.Contained, usesContentPipeline);

                            }
                            else
                            {
                                syncedContentProjectBase.AddContentBuildItem(absoluteName, SyncedProjectRelativeType.Linked, usesContentPipeline);
                            }
                        }
                    }
                    #endregion

                    List<string> filesReferencedByAsset = 
                        FileReferenceManager.Self.GetFilesReferencedBy(absoluteName, EditorObjects.Parsing.TopLevelOrRecursive.Recursive);

                    for (int i = 0; i < filesReferencedByAsset.Count; i++)
                    {
                        if (!filesInModifiedRfs.Contains(filesReferencedByAsset[i]))
                        {
                            filesInModifiedRfs.Add(filesReferencedByAsset[i]);
                        }
                    }
                }
            }
            return usesContentPipeline;
        }

        public static void UpdateTextureFormatFor(ReferencedFileSave rfs)
        {
            string absoluteName = ProjectManager.MakeAbsolute(rfs.Name, true);

            bool usesContentPipeline = rfs.UseContentPipeline || rfs.GetAssetTypeInfo() != null && rfs.GetAssetTypeInfo().MustBeAddedToContentPipeline;

            string parameterTag = GetTextureFormatTag(rfs);
            string valueToSet = rfs.TextureFormat.ToString();

            SetParameterOnBuildItems(absoluteName, parameterTag, valueToSet);
        }

        private static void SetParameterOnBuildItems(string absoluteName, string parameterTag, string valueToSet)
        {
            #region Get the project



            ProjectBase projectBase = ProjectManager.ContentProject;

            if (projectBase == null)
            {
                projectBase = ProjectManager.ProjectBase;
            }

            #endregion

            ProjectItem item = projectBase.GetItem(absoluteName);


            // The item may not be here.  Why wouldn't it be here?  Who knows,
            // could be someone screwed with the .csproj.  Anyway, Glue should
            // be able to survive this, and I don't think this is the place to report
            // a missing file.  We should do that somewhere else - like when we first load
            // the project.
            if (item != null)
            {
                item.SetMetadataValue(parameterTag, valueToSet);
            }

            foreach (ProjectBase syncedProject in ProjectManager.SyncedProjects)
            {
                // Since this is a content file, we want the content project.
                ProjectBase syncedProjectBaseToUse = syncedProject.ContentProject;

                if (syncedProjectBaseToUse == null)
                {
                    syncedProjectBaseToUse = syncedProject;
                }

                item = syncedProjectBaseToUse.GetItem(absoluteName);
                if (item != null)
                {
                    item.SetMetadataValue(parameterTag, valueToSet);
                }
            }
        }

        private static string GetTextureFormatTag(ReferencedFileSave rfs)
        {
            if (rfs.GetAssetTypeInfo() != null && rfs.GetAssetTypeInfo().RuntimeTypeName == "Texture2D")
            {
                return "ProcessorParameters_TextureFormat";
            }
            else
            {
                return "ProcessorParameters_TextureProcessorOutputFormat";
            }

        }

        private static void RemoveFilesFromListReferencedByRfses(List<string> filesInModifiedRfs, List<ReferencedFileSave> allReferencedFiles)
        {
            foreach (ReferencedFileSave possibleReferencer in allReferencedFiles)
            {
                if (!possibleReferencer.UseContentPipeline)
                {
                    string modifiedPossibleReferencerFile = possibleReferencer.Name.ToLower();
                    if (filesInModifiedRfs.Contains(modifiedPossibleReferencerFile))
                    {
                        // There is a RFS referencing this guy
                        filesInModifiedRfs.Remove(modifiedPossibleReferencerFile);
                    }

                    string absoluteFileName = ProjectManager.MakeAbsolute(possibleReferencer.Name);

                    if (File.Exists(absoluteFileName))
                    {
                        List<string> filesInPossibleReferencer =
                            FileReferenceManager.Self.GetFilesReferencedBy(absoluteFileName, TopLevelOrRecursive.Recursive);

                        foreach (string containedFile in filesInPossibleReferencer)
                        {
                            string modifiedContainedFile = ProjectManager.MakeRelativeContent(containedFile).ToLower();

                            if (filesInModifiedRfs.Contains(modifiedContainedFile))
                            {
                                filesInModifiedRfs.Remove(modifiedContainedFile);
                            }
                        }
                    }
                }
            }
        }
    }
}
