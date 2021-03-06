﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using FlatRedBall.Glue.CodeGeneration.CodeBuilder;
using FlatRedBall.Glue.SaveClasses;
using FlatRedBall.Glue.Parsing;
using FlatRedBall.Glue.VSHelpers.Projects;
using FlatRedBall.IO;
using FlatRedBall.Glue.Elements;
using FlatRedBall.Glue.Controls;
using FlatRedBall.Glue.Plugins.Performance;
using FlatRedBall.Glue.Plugins.EmbeddedPlugins.Particle;

namespace FlatRedBall.Glue.CodeGeneration
{
    public enum LoadType
    {
        CompleteLoad,
        MaintainInstance
    }


    internal class ReferencedFileSaveCodeGenerator : ElementComponentCodeGenerator
    {

        // When Entities/Screens write themselves, they need to see if they
        // have content which is referenced by GlobalContent.  If so, then the
        // file in the element shouldn't load itself, but just reference the property
        // in GlobalContent.  This allows users to put files both in GlobalContent as well
        // as in an Element to take advantage of GlobalContent async loading.
        [ThreadStatic]
        public static Dictionary<string, ReferencedFileSave> GlobalContentFilesDictionary = 
            new Dictionary<string, ReferencedFileSave>();


        public static void RefreshGlobalContentDictionary()
        {
            // may be null depending on the thread
            if (GlobalContentFilesDictionary == null)
            {
                GlobalContentFilesDictionary = new Dictionary<string, ReferencedFileSave>();
            }
            GlobalContentFilesDictionary.Clear();

            foreach (ReferencedFileSave rfs in ProjectManager.GlueProjectSave.GlobalFiles)
            {
                try
                {
                    GlobalContentFilesDictionary.Add(rfs.Name, rfs);
                }
                catch(Exception exception)
                {
                    FlatRedBall.Glue.Plugins.PluginManager.ReceiveError("The following file was found twice in the project: " + rfs.Name + "\n\nAdditional info:\n\n" + exception.ToString());
                }
            }

        }



        public override ICodeBlock GenerateFields(ICodeBlock codeBlock,  SaveClasses.IElement element)
        {
            #region Get the ContentManager variable to use

            string contentManagerName = "ContentManagerName";

            if (element is ScreenSave)
            {
                contentManagerName = (element as ScreenSave).ContentManagerForCodeGeneration;
            }

            #endregion

            if (element is EntitySave)
            {
                codeBlock.Line("static object mLockObject = new object();");
                codeBlock.Line("static System.Collections.Generic.List<string> mRegisteredUnloads = new System.Collections.Generic.List<string>();");

                #region Keep track of whether we've already registered an unload method and if StaticContent has been loaded
                codeBlock.Line("static System.Collections.Generic.List<string> LoadedContentManagers = new System.Collections.Generic.List<string>();");
                #endregion
            }

            for (int i = 0; i < element.ReferencedFiles.Count; i++)
            {
                AppendFieldOrPropertyForReferencedFile(codeBlock,
                    element.ReferencedFiles[i], element.Name, element, contentManagerName);

                //stringBuilder.AppendLine(GetFieldForReferencedFile(mSaveObject.ReferencedFiles[i]));

            }

            return codeBlock;
        }

        public override ICodeBlock GenerateInitialize(ICodeBlock codeBlock,  SaveClasses.IElement element)
        {
            for (int i = 0; i < element.ReferencedFiles.Count; i++)
            {
                ReferencedFileSave rfs = element.ReferencedFiles[i];

                // Only do non-static stuff
                if (!rfs.IsSharedStatic)
                {
                    ReferencedFileSaveCodeGenerator.GetInitializationForReferencedFile(rfs, element, codeBlock, element.UseGlobalContent, LoadType.CompleteLoad);

                }
            }

            return codeBlock;
        }

        public override ICodeBlock GenerateAddToManagers(ICodeBlock codeBlock,  SaveClasses.IElement element)
        {
            return codeBlock;
        }

        public override ICodeBlock GenerateDestroy(ICodeBlock codeBlock,  SaveClasses.IElement element)
        {
            for (int i = 0; i < element.ReferencedFiles.Count; i++)
            {
                codeBlock.InsertBlock(GetDestroyForReferencedFile(element, element.ReferencedFiles[i]));
            }
            codeBlock._();

            return codeBlock;
        }

        public override ICodeBlock GenerateActivity(ICodeBlock codeBlock,  SaveClasses.IElement element)
        {
            for (int i = 0; i < element.ReferencedFiles.Count; i++)
            {
                codeBlock.InsertBlock(GetActivityForReferencedFile(element.ReferencedFiles[i], element));
            }

            return codeBlock;
        }

        public override ICodeBlock GenerateAdditionalMethods(ICodeBlock codeBlock,  SaveClasses.IElement element)
        {

            bool inherits = !string.IsNullOrEmpty(element.BaseElement) && !element.InheritsFromFrbType();

            ReferencedFileSaveCodeGenerator.GenerateGetStaticMemberMethod(element.ReferencedFiles, codeBlock, false, inherits);


            string functionName = "GetFile";

            GenerateGetFileMethodByName(element.ReferencedFiles, codeBlock, inherits, functionName, false);




            GenerateGetMember(codeBlock, element);
            return codeBlock;
        }

        private static void GenerateGetMember(ICodeBlock codeBlock, SaveClasses.IElement element)
        {
            #region GetMember (Instance members)

            // GlobalContent is a static class, so it can't have a non-static GetMember method
            // Update June 25, 2011:  This has been moved
            // to a code generator which only works on Elements
            // so we know we're not global content
            //if (!isGlobalContent)
            //{

            var func = codeBlock.Function("object", "GetMember", "string memberName");
            ICodeBlock switchBlock = null;

            bool hasPrependedSwitch = false;

            foreach (ReferencedFileSave rfs in element.ReferencedFiles)
            {
                // Localization databases currently don't create members
                if (rfs.LoadedAtRuntime && !rfs.IsDatabaseForLocalizing &&
                    (rfs.GetAssetTypeInfo() == null || !string.IsNullOrEmpty( rfs.GetAssetTypeInfo().QualifiedRuntimeTypeName.QualifiedType))
                    
                    )
                {

                    if (!hasPrependedSwitch)
                    {
                        // We do the switch here 
                        // so that if there are no
                        // RFS's that the user can get,
                        // we don't want to generate a switch.
                        // This eliminates warnings.
                        hasPrependedSwitch = true;

                        switchBlock = func.Switch("memberName");
                    }

                    AddIfConditionalSymbolIfNecesssary(switchBlock, rfs);
                    string instanceName = rfs.GetInstanceName();

                    switchBlock.CaseNoBreak("\"" + instanceName + "\"")
                               .Line("return " + instanceName + ";");
                    AddEndIfIfNecessary(switchBlock, rfs);
                }
                //stringBuilder.AppendLine("\t\t\t\t\tbreak;");
            }

            func.Line("return null;");
            #endregion

        }

        public override ICodeBlock GenerateLoadStaticContent(ICodeBlock codeBlock,  IElement element)
        {
            var curBlock = codeBlock;
            bool hasAddedRegisterUnloadVariable = false;

            bool needsRegisterUnloadVariable = element is EntitySave && element.UseGlobalContent == false;
            
            if (needsRegisterUnloadVariable)
            {
                curBlock.Line("bool registerUnload = false;");
                hasAddedRegisterUnloadVariable = true;
            }
            if (element is EntitySave)
            {
                curBlock = curBlock.If("LoadedContentManagers.Contains(contentManagerName) == false");
                curBlock.Line("LoadedContentManagers.Add(contentManagerName);");

                AppendAddUnloadMethod(curBlock, element.Name);
            }


            ReferencedFileSaveCodeGenerator.GenerateExceptionForPostInitializeLoads(curBlock);

            for (int i = 0; i < element.ReferencedFiles.Count; i++)
            {
                ReferencedFileSave rfs = element.ReferencedFiles[i];
                
                if (rfs.IsSharedStatic)
                {
                    if (!hasAddedRegisterUnloadVariable)
                    {
                        string name = rfs.Name;
                        bool isRfsInGlobalContent = GlobalContentFilesDictionary.ContainsKey(name);


                        if (rfs.LoadedOnlyWhenReferenced == false &&
                            (!isRfsInGlobalContent || rfs.GetContainerType() == ContainerType.None) &&
                            needsRegisterUnloadVariable)
                        {
                            curBlock.Line("bool registerUnload = false;");
                            hasAddedRegisterUnloadVariable = true;
                        }
                    }
                    GetInitializationForReferencedFile(rfs, element, curBlock, element.UseGlobalContent, LoadType.CompleteLoad);
                }
            }


            if (element is EntitySave)
            {
                curBlock = curBlock.End();
            }


            return curBlock;
        }

        public override ICodeBlock GenerateUnloadStaticContent(ICodeBlock codeBlock,  IElement element)
        {
            // We'll assume that we want to get rid of the last one
            // The user may have loaded content from an Entity without calling LoadStaticContent - like if the
            // Entity is a LoadedOnlyWhenReferenced container.  In that case, we need to make sure we account for
            // that by only removing if there are actually loaded content managers
            codeBlock = codeBlock.If("LoadedContentManagers.Count != 0");
            codeBlock.Line("LoadedContentManagers.RemoveAt(0);");
            codeBlock.Line("mRegisteredUnloads.RemoveAt(0);");
            codeBlock = codeBlock.End();

            codeBlock = codeBlock.If("LoadedContentManagers.Count == 0");

            for (int i = 0; i < element.ReferencedFiles.Count; i++)
            {
                if (element.ReferencedFiles[i].IsSharedStatic && element.ReferencedFiles[i].GetGeneratesMember())
                {
                    ReferencedFileSave rfs = element.ReferencedFiles[i];
                    AddIfConditionalSymbolIfNecesssary(codeBlock, rfs);

                    string variableName = rfs.GetInstanceName();
                    string fieldName = variableName;

                    if (rfs.LoadedOnlyWhenReferenced)
                    {
                        fieldName = "m" + variableName;
                    }

                    AssetTypeInfo ati = rfs.GetAssetTypeInfo();
                    codeBlock = codeBlock
                        .If(fieldName + " != null");
                    // Vic says - since now static referenced file saves just represent the last-loaded, we don't
                    // want them to be removed anymore.
                    //stringBuilder.AppendLine(string.Format("{0}{1};", tabs, ati.DestroyMethod.Replace("this", variableName)));
                    //stringBuilder.AppendLine(string.Format("{0}{1} = null;", tabs, fieldName));
                    // But we do want to null them out if they are loaded only when referenced
                    // Update on May 2, 2011
                    // This is actually a problem with Entities because
                    // you can move from Screen to Screen and accumulate
                    // static references to objects which don't get removed
                    // which can cause some pretty nasty accumulation errors.
                    // Therefore, I really do think we should always call Destroy and remove the object.
                    // I can't think of why we wouldn't, actually.  Perhaps the comment above was written
                    // before we had a very advanced pattern in place.  
                    //if (rfs.LoadedOnlyWhenReferenced)
                    {
                        // We want to call destroy on the object,
                        // but some types, like the AnimationChainList,
                        // don't have destroy methods.  Therefore, we should
                        // check if destroy exists and append a destroy call if
                        // so.
                        // Files like .CSV may 
                        // be part of an Entity
                        // but these will not have
                        // an AssetTypeInfo, so we need
                        // to check for that.
                        if (ati != null &&  !string.IsNullOrEmpty(ati.DestroyMethod))
                        {
                            codeBlock.Line(ati.DestroyMethod.Replace("this", fieldName) + ";");
                        }
                        codeBlock.Line(fieldName + "= null;");
                    }

                    codeBlock = codeBlock.End();
                    AddEndIfIfNecessary(codeBlock, rfs);
                }
            }

            codeBlock = codeBlock.End();

            return codeBlock;
        }

        public static void AppendFieldOrPropertyForReferencedFile(ICodeBlock codeBlock,  ReferencedFileSave referencedFile,
            string containerName, IElement element)
        {
            AppendFieldOrPropertyForReferencedFile(codeBlock, referencedFile, containerName, element, "ContentManagerName");
        }

        public static void AppendFieldOrPropertyForReferencedFile(ICodeBlock codeBlock,  ReferencedFileSave referencedFile,
            string containerName, IElement element, string contentManagerName)
        {
            /////////////////////////////////////EARLY OUT//////////////////////////////////////////////
            // If the referenced file is a database for localizing, it will just be stuffed right into the localization manager
            if (!referencedFile.LoadedAtRuntime || referencedFile.IsDatabaseForLocalizing) return;
            ///////////////////////////////////END EARLY OUT/////////////////////////////////////////////

            string fileName = referencedFile.Name;
            string extension = FileManager.GetExtension(fileName);
            AssetTypeInfo ati = referencedFile.GetAssetTypeInfo();

            string variableName = referencedFile.GetInstanceName();


            #region Get the typeName
            string typeName = null;

            if (ati != null && !referencedFile.TreatAsCsv && ati.QualifiedRuntimeTypeName.QualifiedType != null)
            {
                typeName = ati.QualifiedRuntimeTypeName.QualifiedType;
            }
            else if (extension == "csv" || referencedFile.TreatAsCsv)
            {
                typeName = CsvCodeGenerator.GetEntireGenericTypeForCsvFile(referencedFile);
            }

            #endregion

            //////////////////////////////EARLY OUT///////////////////////////////////////
            if (typeName == null) return;
            ///////////////////////////END EARLY OUT//////////////////////////////////////

            AddIfConditionalSymbolIfNecesssary(codeBlock, referencedFile);

            if (NeedsFullProperty(referencedFile, containerName))
            {
                AppendPropertyForReferencedFileSave(codeBlock, referencedFile, containerName, element, contentManagerName, ati, variableName, typeName);
            }
            else
            {
                if (containerName == ContentLoadWriter.GlobalContentContainerName)
                {
                    // Global Content will always have the content as properties.  This is so that you can switch between
                    // async and sync loading and not have to change reflection code
                    codeBlock.AutoProperty(variableName, 
                                           Public: referencedFile.HasPublicProperty,
                                            // Should be protected so derived classes can access this
                                            Protected: !referencedFile.HasPublicProperty,

                                           Static: referencedFile.IsSharedStatic, 
                                           Type: typeName);
                }
                else
                {
                    codeBlock.Line(StringHelper.Modifiers(
                        Public: referencedFile.HasPublicProperty,
                        // Should be protected so derived classes can access this
                        Protected: !referencedFile.HasPublicProperty,
                        Static: referencedFile.IsSharedStatic,
                        Type: typeName,
                        Name: variableName) + ";");
                }

            }

            AddEndIfIfNecessary(codeBlock, referencedFile);

        }

        private static void AppendPropertyForReferencedFileSave(ICodeBlock codeBlock, ReferencedFileSave referencedFile, string containerName, IElement element, string contentManagerName, AssetTypeInfo ati, string variableName, string typeName)
        {

            codeBlock.Line(StringHelper.Modifiers(Static: referencedFile.IsSharedStatic, Type: typeName, Name: "m" + variableName) + ";");

            // No need to use
            // ManualResetEvents
            // if the ReferencedFileSave
            // is LoadedOnlyWhenReferenced.

            bool shouldBlockThreads = containerName ==
                ContentLoadWriter.GlobalContentContainerName && !referencedFile.LoadedOnlyWhenReferenced;

            if (shouldBlockThreads)
            {
                codeBlock.Line("#if !REQUIRES_PRIMARY_THREAD_LOADING");

                codeBlock.Line("//Blocks the thread on request of " + variableName + " until it has been loaded");
                codeBlock.Line("static ManualResetEvent m" + variableName + "Mre = new ManualResetEvent(false);");

                codeBlock.Line("// Used to lock getter and setter so that " + variableName + " can be set on any thread even if its load is in progrss");
                codeBlock.Line("static object m" + variableName + "_Lock = new object();");
                codeBlock.Line("#endif");

            }

            string lastContentManagerVariableName = "mLastContentManagerFor" + variableName;
            if (referencedFile.LoadedAtRuntime && element != null)
            {
                codeBlock.Line("static string " + lastContentManagerVariableName + ";");
            }

            // Silverlight and Windows Phone only allow reflection on public methods
            // Since it's common practice to use reflection to reference LoadedOnlyWhenReferenced
            // properties, we need to make them public.
            var propBlock = codeBlock.Property(variableName, Public: true, Static: referencedFile.IsSharedStatic, Type: typeName);

            var getBlock = propBlock.Get();

            if (referencedFile.LoadedOnlyWhenReferenced)
            {
                WriteLoadedOnlyWhenReferencedPropertyBody(referencedFile, containerName, element, contentManagerName, ati, variableName, lastContentManagerVariableName, getBlock);
            }
            else if (containerName == ContentLoadWriter.GlobalContentContainerName)
            {
                #region Write the getter


                getBlock.Line("#if !REQUIRES_PRIMARY_THREAD_LOADING");

                if (shouldBlockThreads)
                {
                    getBlock = getBlock.Lock("m" + variableName + "_Lock");
                }

                //Perform a WaitOne on the event with a timeout value of zero.
                // It will return true if the event is not set, or false if the timeout occurs. 
                // In other words, false -> event is set, true -> event is not set.
                getBlock.Line("bool isBlocking = !m" + variableName + "Mre.WaitOne(0);");

                {
                    var ifBlock = getBlock.If("isBlocking");

                    // This is our way of telling the GlobalContentManager to hurry up - we're waiting
                    // on some content!
                    ifBlock.Line("RequestContentLoad(\"" + referencedFile.Name + "\");");

                    #region If RecordLockRecord - write the code for recording the load order so that it can be optimized

                    if (ProjectManager.GlueProjectSave.GlobalContentSettingsSave.RecordLockContention)
                    {
                        ifBlock.Line("LockRecord.Add(\"\\n" + variableName + "\");");
                    }

                    #endregion
                }

                getBlock.Line("m" + variableName + "Mre.WaitOne();");
                getBlock.Line("return m" + variableName + ";");
                if (shouldBlockThreads)
                {
                    getBlock = getBlock.End();
                }
                getBlock.Line("#else");
                WriteLoadedOnlyWhenReferencedPropertyBody(referencedFile, containerName, element, contentManagerName, ati, variableName, lastContentManagerVariableName, getBlock);


                getBlock.Line("#endif");

                #endregion

                #region Write the setter



                var setBlock = propBlock.Set();

                setBlock.Line("#if !REQUIRES_PRIMARY_THREAD_LOADING");

                if (shouldBlockThreads)
                {
                    setBlock = setBlock.Lock("m" + variableName + "_Lock");
                }

                WriteAssignmentAndMreSet(variableName, setBlock);
                if (shouldBlockThreads)
                {
                    setBlock = setBlock.End();
                }
                setBlock.Line("#else");
                setBlock.Line("m" + variableName + " = value;");


                setBlock.Line("#endif");
                #endregion

            }
            else
            {
                string fieldName = "m" + variableName;

                getBlock.Line("#if REQUIRES_PRIMARY_THREAD_LOADING");
                
                var ifBlock = getBlock.If("fieldName == null && FlatRedBall.FlatRedBallServices.IsThreadPrimary()");
                ifBlock.Line("FlatRedBall.FlatRedBallServices.GetContentManagerByName(ContentManager).ProcessTexturesWaitingToBeLoaded();");
                
                getBlock.Line("#endif");

                getBlock.Line("return " + fieldName + ";");
            }
        }

        private static bool NeedsFullProperty(ReferencedFileSave referencedFile, string containerName)
        {

            return (ProjectManager.GlueProjectSave.GlobalContentSettingsSave.LoadAsynchronously &&
                    containerName == ContentLoadWriter.GlobalContentContainerName) || 
                             referencedFile.LoadedOnlyWhenReferenced;
        }

        private static void WriteAssignmentAndMreSet(string variableName, ICodeBlock setBlock)
        {
            setBlock.Line("m" + variableName + " = value;");
            setBlock.Line("m" + variableName + "Mre.Set();");
        }

        public static void GetLoadCallForAtiFile(ReferencedFileSave rfs, AssetTypeInfo ati, string variableName, string contentManagerString, string fileNameToLoad, ICodeBlock codeBlock)
        {
            string formattableLine = null;

            if (!string.IsNullOrEmpty(ati.CustomLoadMethod))
            {
                formattableLine = ati.CustomLoadMethod;
                // Replace the expressive variable names with ints:
                formattableLine = formattableLine.Replace("{THIS}", "{0}");
                formattableLine = formattableLine.Replace("{TYPE}", "{1}");
                formattableLine = formattableLine.Replace("{FILE_NAME}", "{2}");
                formattableLine = formattableLine.Replace("{CONTENT_MANAGER_NAME}", "{3}");

                // If the user didn't end the line with a semicolon, let's do it ourselves
                if (!formattableLine.EndsWith(";"))
                {
                    formattableLine = formattableLine + ";";
                }
            }
            else
            {

                formattableLine = "{0} = FlatRedBall.FlatRedBallServices.Load<{1}>(@\"{2}\", {3});";
            }

            bool shouldAddExtensionOnNonXnaPlatforms = 
                FileManager.GetExtension(fileNameToLoad) != FileManager.GetExtension(rfs.Name) && FileManager.GetExtension(rfs.Name) == "png";

            if(shouldAddExtensionOnNonXnaPlatforms)
            {
                codeBlock.Line("#if IOS || ANDROID || WINDOWS_8");

                string withExtension = fileNameToLoad + ".png";
                codeBlock.Line(
                    string.Format(formattableLine,
                                variableName, ati.QualifiedRuntimeTypeName.QualifiedType, withExtension, contentManagerString));
                codeBlock.Line("#else");

            }

            codeBlock.Line(
                string.Format(formattableLine,
                            variableName, ati.QualifiedRuntimeTypeName.QualifiedType, fileNameToLoad, contentManagerString));

            if (shouldAddExtensionOnNonXnaPlatforms)
            {
                codeBlock.Line("#endif");
            }

        }

        private static void WriteLoadedOnlyWhenReferencedPropertyBody(ReferencedFileSave referencedFile, string containerName, IElement element, 
            string contentManagerName, AssetTypeInfo ati, string variableName, string lastContentManagerVariableName, ICodeBlock getBlock)
        {
            string referencedFileName = GetFileToLoadForRfs(referencedFile, ati);


            string mThenVariableName = "m" + variableName;

            ICodeBlock ifBlock = null;
            if (element == null)
            {
                ifBlock = getBlock.If(mThenVariableName + " == null");
            }
            else
            {
                string contentManagerToCompareAgainst;

                if (element is ScreenSave)
                {
                    contentManagerToCompareAgainst = "\"" + FileManager.RemovePath(element.Name) + "\"";
                }
                else
                {
                    contentManagerToCompareAgainst = "ContentManagerName";
                }

                string conditionContents =
                    mThenVariableName + " == null || " + lastContentManagerVariableName + " != " + contentManagerToCompareAgainst;

                bool isDisposable = referencedFile.RuntimeType == "Texture2D" ||
                    referencedFile.RuntimeType == "Microsoft.Xna.Framework.Graphics.Texture2D";
                if (isDisposable)
                {
                    conditionContents += " || " + mThenVariableName + ".IsDisposed";
                }

                ifBlock = getBlock.If(conditionContents);
                ifBlock.Line(lastContentManagerVariableName + " = " + contentManagerToCompareAgainst + ";");
            }

            if (containerName != ContentLoadWriter.GlobalContentContainerName)
            {
                GenerateExceptionForPostInitializeLoads(getBlock);
            }

            PerformancePluginCodeGenerator.CodeBlock = ifBlock;
            PerformancePluginCodeGenerator.SaveObject = element;
            PerformancePluginCodeGenerator.GenerateStart(" Get " + variableName);
            if (ati != null && !referencedFile.IsCsvOrTreatedAsCsv)
            {
                GetLoadCallForAtiFile(referencedFile,
                    ati, mThenVariableName, contentManagerName,
                    ProjectBase.AccessContentDirectory + referencedFileName, ifBlock);
            }
            else if (referencedFile.IsCsvOrTreatedAsCsv)
            {
                GenerateCsvDeserializationCode(referencedFile, ifBlock, mThenVariableName,
                                                referencedFileName, LoadType.CompleteLoad);
            }


            if (element != null && element is EntitySave)
            {
                AppendAddUnloadMethod(ifBlock, containerName);
            }
            PerformancePluginCodeGenerator.GenerateEnd();

            getBlock.Line("return " + mThenVariableName + ";");
        }

        public static void AddEndIfIfNecessary(ICodeBlock codeBlock, ReferencedFileSave referencedFile)
        {
            if (!string.IsNullOrEmpty(referencedFile.ConditionalCompilationSymbols))
            {
                codeBlock.Line("#endif");
            }
        }

        public static void AddIfConditionalSymbolIfNecesssary(ICodeBlock codeBlock, ReferencedFileSave referencedFile)
        {
            if (!string.IsNullOrEmpty(referencedFile.ConditionalCompilationSymbols))
            {
                codeBlock.Line("#if " + referencedFile.ConditionalCompilationSymbols);
            }
        }

        private static string GetFileToLoadForRfs(ReferencedFileSave referencedFile, AssetTypeInfo ati)
        {
            string referencedFileName = referencedFile.Name;

            if ((ati != null && ati.MustBeAddedToContentPipeline) || referencedFile.UseContentPipeline)
            {
                referencedFileName = FileManager.RemoveExtension(referencedFileName);
            }

            if (ProjectManager.ContentProject != null)
            {
                referencedFileName = ProjectManager.ContentProject.ContainedFilePrefix + referencedFileName;
            }

            referencedFileName = referencedFileName.ToLower();
            return referencedFileName;
        }

        public static void AppendAddUnloadMethod(ICodeBlock codeBlock, string saveObjectName)
        {
            var lockBlock = codeBlock.Lock("mLockObject");

            var ifBlock =
                lockBlock.If("!mRegisteredUnloads.Contains(ContentManagerName) && ContentManagerName != FlatRedBall.FlatRedBallServices.GlobalContentManager");

            ifBlock.Line("FlatRedBall.FlatRedBallServices.GetContentManagerByName(ContentManagerName).AddUnloadMethod(\"" +
                         FileManager.RemovePath(saveObjectName) +
                         "StaticUnload\", UnloadStaticContent);");

            ifBlock.Line("mRegisteredUnloads.Add(ContentManagerName);");
        }

        public static void GetReload(ReferencedFileSave referencedFile, IElement container,
            ICodeBlock codeBlock, bool loadsUsingGlobalContentManager, LoadType loadType)
        {
            bool shouldGenerateInitialize = GetIfShouldGenerateInitialize(referencedFile);

            if(shouldGenerateInitialize)
            {
                if(referencedFile.IsCsvOrTreatedAsCsv && referencedFile.CreatesDictionary)
                {
                    var fileName = ProjectBase.AccessContentDirectory + referencedFile.Name.ToLower().Replace("\\", "/");
                    var instanceName = referencedFile.GetInstanceName();
                    string line =
                        $"FlatRedBall.IO.Csv.CsvFileManager.UpdateDictionaryValuesFromCsv({instanceName}, \"{fileName}\");";
                    codeBlock.Line(line);
                }
                else
                {
                    GetInitializationForReferencedFile(referencedFile, container, codeBlock, loadsUsingGlobalContentManager, loadType);
                }

            }
        }

        public static void GetInitializationForReferencedFile(ReferencedFileSave referencedFile, IElement container, 
            ICodeBlock codeBlock,  bool loadsUsingGlobalContentManager, LoadType loadType)
        {
            #region early-outs (not loaded at runtime, loaded only when referenced)

            bool shouldGenerateInitialize = GetIfShouldGenerateInitialize(referencedFile);

            if (!shouldGenerateInitialize)
            {
                return;
            }

            #endregion

            // I'm going to only do this if we're non-null so that we don't add it for global content.  Global Content may load
            // async and cause bad data
            if (container != null)
            {
                PerformancePluginCodeGenerator.GenerateStart(container, codeBlock, "LoadStaticContent" + FileManager.RemovePath(referencedFile.Name));
            }
            AddIfConditionalSymbolIfNecesssary(codeBlock, referencedFile);

            bool directives = false;

            for (int i = referencedFile.ProjectSpecificFiles.Count; i >= 0; i--)
            {
                bool isProjectSpecific = i != 0;

                string fileName;
                ProjectBase project;

                if (isProjectSpecific)
                {
                    fileName = referencedFile.ProjectSpecificFiles[i - 1].FilePath.ToLower().Replace("\\", "/");

                    // At one point
                    // the project specific
                    // files were platform specific
                    // but instead we want them to be
                    // based off of the project name instead.
                    // The reason for this is because a user could
                    // create a synced project that targets the same
                    // platform.  
                    project = ProjectManager.GetProjectByName(referencedFile.ProjectSpecificFiles[i - 1].ProjectId);
                    if (project == null)
                    {
                        project = ProjectManager.GetProjectByTypeId(referencedFile.ProjectSpecificFiles[i - 1].ProjectId);
                    }
                }
                else
                {
                    fileName = referencedFile.Name.ToLower().Replace("\\", "/");
                    project = ProjectManager.ProjectBase;
                }

                string containerName = ContentLoadWriter.GlobalContentContainerName;
                if (container != null)
                {
                    containerName = container.Name;
                }
                AddCodeforFileLoad(referencedFile, ref codeBlock, loadsUsingGlobalContentManager,
                    ref directives, isProjectSpecific, ref fileName, project, loadType, containerName);
            }

            if (directives == true)
            {
                codeBlock = codeBlock.End()
                    .Line("#endif");
            }

            AddEndIfIfNecessary(codeBlock, referencedFile);
            // See above why this if-statement exists
            if (container != null)
            {
                PerformancePluginCodeGenerator.GenerateEnd(container, codeBlock, "LoadStaticContent" + FileManager.RemovePath(referencedFile.Name));
            }
        }

        private static bool GetIfShouldGenerateInitialize(ReferencedFileSave referencedFile)
        {
            bool shouldGenerateInitialize = true;

            if (referencedFile.LoadedOnlyWhenReferenced)
            {
                shouldGenerateInitialize = false;
            }

            if (referencedFile.IsDatabaseForLocalizing == false && !referencedFile.GetGeneratesMember())
            {
                shouldGenerateInitialize = false; // There is no qualified type to load to, so let's not generate code to load it
            }

            if (referencedFile.IsDatabaseForLocalizing && !referencedFile.LoadedAtRuntime)
            {
                shouldGenerateInitialize = false;
            }

            return shouldGenerateInitialize;
        }

        private static void AddCodeforFileLoad(ReferencedFileSave referencedFile, ref ICodeBlock codeBlock, 
            bool loadsUsingGlobalContentManager, ref bool directives, bool isProjectSpecific, 
            ref string fileName, ProjectBase project, LoadType loadType, string containerName)
        {
            if (project != null)
            {

                if (ProjectManager.IsContent(fileName))
                {
                    fileName = (ProjectManager.ContentProject.ContainedFilePrefix + fileName).ToLower();
                }

                if (isProjectSpecific)
                {
                    if (directives == true)
                    {
                        codeBlock = codeBlock.End()
                            .Line("#elif " + project.PrecompilerDirective)
                            .CodeBlockIndented();
                    }
                    else
                    {
                        directives = true;
                        codeBlock = codeBlock
                            .Line("#if " + project.PrecompilerDirective)
                            .CodeBlockIndented();
                    }
                }
                else
                {
                    if (directives == true)
                    {
                        codeBlock = codeBlock.End()
                            .Line("#else")
                            .CodeBlockIndented();
                    }
                }

                if (referencedFile.IsDatabaseForLocalizing)
                {
                    GenerateCodeForLocalizationDatabase(referencedFile, codeBlock, fileName, loadType);
                }
                else
                {
                    AssetTypeInfo ati = referencedFile.GetAssetTypeInfo();

                    // I think we can set the field rather than the property, and then Set() the MRE if necessary afterwards:
                    //string variableName = referencedFile.GetInstanceName();

                    string variableName = null;
                    if (NeedsFullProperty(referencedFile, containerName))
                    {
                        variableName = "m" + referencedFile.GetInstanceName();
                    }
                    else
                    {
                        variableName = referencedFile.GetInstanceName();
                    }

                    if (!referencedFile.IsCsvOrTreatedAsCsv && ati != null)
                    {
                        // If it's not a CSV, then we only support loading if the load type is complete
                        // I don't know if I'll want to change this (or if I can) in the future.
                        if (loadType == LoadType.CompleteLoad)
                        {
                            GenerateInitializationForAssetTypeInfoRfs(referencedFile, codeBlock, loadsUsingGlobalContentManager, variableName, fileName, ati, project);
                        }
                    }
                    else if(referencedFile.IsCsvOrTreatedAsCsv)
                    {
                        GenerateInitializationForCsvRfs(referencedFile, codeBlock, variableName, fileName, loadType);
                    }
                }


                NamedObjectSaveCodeGenerator.WriteTextSpecificInitialization(referencedFile, codeBlock);
            }
        }

        private static void GenerateInitializationForCsvRfs(ReferencedFileSave referencedFile, ICodeBlock codeBlock, string variableName, string fileName, LoadType loadType)
        {
            var curBlock = codeBlock;

            // If it's not 
            // a complete reload
            // then we don't care
            // about whether it's null
            // or not.
            if (referencedFile.IsSharedStatic && loadType == LoadType.CompleteLoad)
            {
                // Modify this line of code to use mVariable name if it's a global content and if we're doing async loading.
                // The reason is we can't query the property, because that would result in the property waiting until the content
                // is done loading - which would lock the game.
                // Update March 29, 2014
                // Global content will now feed the variable name with 'm' prefixed
                // so we don't have to branch here anymore:
                //if (referencedFile.GetContainerType() == ContainerType.None && ProjectManager.GlueProjectSave.GlobalContentSettingsSave.LoadAsynchronously)
                //{
                //    curBlock = codeBlock.If(string.Format("{0} == null", variableName));
                //}
                //else
                //{
                //    curBlock = codeBlock.If(string.Format("{0} == null", variableName));
                //}
                curBlock = codeBlock.If(string.Format("{0} == null", variableName));

            }

            GenerateCsvDeserializationCode(referencedFile, curBlock, variableName, fileName, loadType);
        }

        private static void GenerateInitializationForAssetTypeInfoRfs(ReferencedFileSave referencedFile, ICodeBlock codeBlock, bool loadsUsingGlobalContentManager, string variableName, string fileName, AssetTypeInfo ati, ProjectBase project)
        {
            string typeName = ati.RuntimeTypeName;
            var vsProject = project as VisualStudioProject;
            var isContentPipeline = (vsProject == null || vsProject.AllowContentCompile) &&
                                    (ati.MustBeAddedToContentPipeline || referencedFile.UseContentPipeline);

            if (isContentPipeline)
            {
                fileName = FileManager.RemoveExtension(fileName);
            }

            bool isSharedStatic = referencedFile.IsSharedStatic;


            string referencedFileName = referencedFile.Name;
            bool containedByGlobalContentFiles = GlobalContentFilesDictionary.ContainsKey(referencedFileName);

            var referencedFileContainerType = referencedFile.GetContainerType();

            if (isSharedStatic &&
                containedByGlobalContentFiles && 
                loadsUsingGlobalContentManager &&
                referencedFileContainerType != ContainerType.None)
            {
                string globalRfsVariable = GlobalContentFilesDictionary[referencedFile.Name].GetInstanceName();

                codeBlock.Line(string.Format("{0} = GlobalContent.{1};",
                                                variableName, globalRfsVariable));
            }
            else
            {
                ContentLoadWriter.GetLoadCallsForRfs(
                    referencedFile, fileName, ati, variableName, codeBlock, loadsUsingGlobalContentManager && referencedFile.IsSharedStatic, isContentPipeline);
            }
        }

        private static void GenerateCodeForLocalizationDatabase(ReferencedFileSave referencedFile, ICodeBlock codeBlock, string fileName, LoadType loadType)
        {
            char delimiter = '\0';

            delimiter = referencedFile.CsvDelimiter.ToChar();

            if (loadType == LoadType.MaintainInstance)
            {
                throw new Exception("Reloading database isn't allowed");
            }

            codeBlock.Line(
                string.Format("LocalizationManager.AddDatabase(\"{0}\", '{1}');", "content/" + fileName, delimiter));
        }

        private static void GenerateCsvDeserializationCode(ReferencedFileSave referencedFile, ICodeBlock codeBlock,  string variableName, string fileName, LoadType loadType)
        {
            #region Get the typeName (type as a string)

            string typeName;

            if (FileManager.GetExtension(fileName) == "csv" || referencedFile.TreatAsCsv)
            {
                // The CustomClass interface keeps a name just as it appears in Glue, so we want to use
                // the referencedFile.Name instead of the fileName because fileName will have "Content/" on it
                // and this shouldn't be the case for XNA 4 games
                typeName = CsvCodeGenerator.GetEntireGenericTypeForCsvFile(referencedFile);
            }

            else
            {
                typeName = "System.Collections.Generic.List<" + FileManager.RemovePath(FileManager.RemoveExtension(fileName)) + ">";
            }

            if (typeName.ToLower().EndsWith("file"))
            {
                typeName = typeName.Substring(0, typeName.Length - "file".Length);
            }
            #endregion

            #region Apply the delimiter change

            // Use the delimiter specified in Glue

            var block = codeBlock.Block();
            block.Line("// We put the { and } to limit the scope of oldDelimiter");
            block.Line("char oldDelimiter = FlatRedBall.IO.Csv.CsvFileManager.Delimiter;");

            char delimiterAsChar = referencedFile.CsvDelimiter.ToChar();

            block.Line(@"FlatRedBall.IO.Csv.CsvFileManager.Delimiter = '" + delimiterAsChar + "';");

            #endregion

            string whatToLoadInto;
            if (loadType == LoadType.CompleteLoad)
            {
                whatToLoadInto = "temporaryCsvObject";
                block.Line(string.Format("{0} {1} = new {0}();", typeName, whatToLoadInto));
            }
            else
            {
                whatToLoadInto = referencedFile.GetInstanceName();
                block.Line(string.Format("{0}.Clear();", whatToLoadInto));
            }

            #region Call CsvFileManager.CsvDeserializeList/Dictionary

            if (referencedFile.CreatesDictionary)
            {
                string keyType;
                string valueType;

                CsvCodeGenerator.GetDictionaryTypes(referencedFile, out keyType, out valueType);

                if (keyType == null)
                {
                    System.Windows.Forms.MessageBox.Show("Could not find the key type for:\n\n" + referencedFile.Name + "\n\nYou need to mark one of the headers as required or not load this file as a dictionary.");
                    keyType = "UNKNOWN_TYPE";

                }
                // CsvFileManager.CsvDeserializeDictionary<string, CarData>("Content/CarData.csv", carDataDictionary);
                block.Line(string.Format("FlatRedBall.IO.Csv.CsvFileManager.CsvDeserializeDictionary<{2}, {3}>(\"{0}\", {1});", ProjectBase.AccessContentDirectory + fileName,
                                  whatToLoadInto, keyType, valueType));
            }
            else
            {
                string elementType = referencedFile.GetTypeForCsvFile();

                block.Line(string.Format("FlatRedBall.IO.Csv.CsvFileManager.CsvDeserializeList(typeof({0}), \"{1}\", {2});",
                                         elementType, ProjectBase.AccessContentDirectory + fileName, whatToLoadInto));
            }

            #endregion

            block.Line("FlatRedBall.IO.Csv.CsvFileManager.Delimiter = oldDelimiter;");

            if (loadType == LoadType.CompleteLoad)
            {
                block.Line(string.Format("{0} = temporaryCsvObject;", variableName));
            }
        }

        static ICodeBlock GetActivityForReferencedFile(ReferencedFileSave referencedFile, IElement element)
        {
            ICodeBlock codeBlock = new CodeDocument();
            /////////////////////EARLY OUT/////////////////////////
            if (!referencedFile.LoadedAtRuntime)
            {
                return codeBlock;
            }
            //////////////////END EARLY OUT//////////////////////

            AssetTypeInfo ati = referencedFile.GetAssetTypeInfo();


            // If it's an emitter, call TimedEmit:
            ParticleCodeGenerator.GenerateTimedEmit(codeBlock, referencedFile, element);

            if (ati != null && (!referencedFile.IsSharedStatic || element is ScreenSave )&& !referencedFile.LoadedOnlyWhenReferenced && ati.ActivityMethod != null)
            {
                AddIfConditionalSymbolIfNecesssary(codeBlock, referencedFile);
                string variableName = referencedFile.GetInstanceName();

                codeBlock.Line(ati.ActivityMethod.Replace("this", variableName) + ";");

                AddEndIfIfNecessary(codeBlock, referencedFile);
                return codeBlock;
            }
            else
            {
                return codeBlock;
            }
        }

        public static ICodeBlock GetPostInitializeForReferencedFile(ReferencedFileSave referencedFile)
        {
            AssetTypeInfo ati = referencedFile.GetAssetTypeInfo();
            ICodeBlock codeBlock = new CodeDocument();

            if (ati != null && !referencedFile.IsSharedStatic)
            {
                string variableName = referencedFile.GetInstanceName();
                if (!string.IsNullOrEmpty(ati.PostInitializeCode))
                {
                    codeBlock.Line(ati.PostInitializeCode.Replace("this", variableName) + ";");
                }
            }


            return codeBlock;
        }

        public static ICodeBlock GetPostCustomActivityForReferencedFile(ReferencedFileSave referencedFile)
        {
            ICodeBlock codeBlock = new CodeDocument();

            if (!referencedFile.LoadedAtRuntime)
            {
                return codeBlock;
            }

            AssetTypeInfo ati = referencedFile.GetAssetTypeInfo();

            // January 8, 2012
            // This used to not
            // check LoadedOnlyWhenReferenced
            // but I think it should otherwise
            // this code will always load Scenes
            // that are LoadedOnlyWhenReferenced by
            // calling their ManageAll method - so I
            // added a check for LoadedOnlyWhenReferenced.
            if (ati != null && !referencedFile.IsSharedStatic && !referencedFile.LoadedOnlyWhenReferenced && ati.AfterCustomActivityMethod != null)
            {
                AddIfConditionalSymbolIfNecesssary(codeBlock, referencedFile);
                string variableName = referencedFile.GetInstanceName();

                codeBlock.Line(ati.AfterCustomActivityMethod.Replace("this", variableName) + ";");
                AddEndIfIfNecessary(codeBlock, referencedFile);
                return codeBlock;
            }
            else
            {
                return codeBlock;
            }
        }

        protected ICodeBlock GetDestroyForReferencedFile(IElement element, ReferencedFileSave referencedFile)
        {
            ICodeBlock codeBlock = new CodeDocument(3);

            ///////////////////////////////EARLY OUT///////////////////////
            if (!referencedFile.LoadedAtRuntime || !referencedFile.DestroyOnUnload)
            {
                return codeBlock;
            }

            if (referencedFile.GetGeneratesMember() == false)
            {
                return codeBlock;
            }

            /////////////////////////////END EARLY OUT/////////////////////

            AddIfConditionalSymbolIfNecesssary(codeBlock, referencedFile);

            string fileName = referencedFile.Name;
            AssetTypeInfo ati = referencedFile.GetAssetTypeInfo();
            string variableName = referencedFile.GetInstanceName();

            bool isScreenSave = element is ScreenSave;
            if (referencedFile.LoadedOnlyWhenReferenced)
            {
                variableName = "m" + variableName;
                codeBlock = codeBlock.If(variableName + " != null");
            }
            if (ati != null && (!referencedFile.IsSharedStatic || isScreenSave))
            {
                string typeName = ati.RuntimeTypeName;
                string destroyMethod = ati.DestroyMethod;
                string recycleMethod = ati.RecycledDestroyMethod;
                if (string.IsNullOrEmpty(recycleMethod))
                {
                    recycleMethod = destroyMethod;
                }



                if (!string.IsNullOrEmpty(ati.DestroyMethod))
                {
                    if (isScreenSave && recycleMethod != destroyMethod)
                    {
                        codeBlock = codeBlock.If("this.UnloadsContentManagerWhenDestroyed && ContentManagerName != \"Global\"");
                        codeBlock.Line(destroyMethod.Replace("this", variableName) + ";");
                        if (referencedFile.LoadedOnlyWhenReferenced)
                        {
                            codeBlock = codeBlock.End().ElseIf(variableName + " != null");
                        }
                        else
                        {
                            codeBlock = codeBlock.End().Else();
                        }
                        codeBlock.Line(recycleMethod.Replace("this", variableName) + ";");
                        codeBlock = codeBlock.End();

                    }
                    else
                    {
                        codeBlock.Line(destroyMethod.Replace("this", variableName) + ";");
                    }
                }

                if (ati.ShouldBeDisposed && element.UseGlobalContent == false)
                {
                    codeBlock = codeBlock.If("this.UnloadsContentManagerWhenDestroyed && ContentManagerName != \"Global\"");
                    codeBlock.Line(string.Format("{0}.Dispose();", variableName));
                    codeBlock = codeBlock.End();
                }

                
            }

            if (element is ScreenSave && referencedFile.IsSharedStatic)
            {
                if (referencedFile.LoadedOnlyWhenReferenced)
                {
                    variableName = "m" + referencedFile.GetInstanceName();
                }
                // We used to do this here, but we want to do it after all Objects have been destroyed
                // because we may need to make the file one way before the destruction of the objects.

                if (ati != null && ati.SupportsMakeOneWay)
                {
                    codeBlock = codeBlock.If("this.UnloadsContentManagerWhenDestroyed && ContentManagerName != \"Global\"");
                    codeBlock.Line(string.Format("{0} = null;", variableName));
                    codeBlock = codeBlock.End().Else();
                    codeBlock.Line(string.Format("{0}.MakeOneWay();", variableName));
                    codeBlock = codeBlock.End();

                }
                else
                {
                    codeBlock.Line(string.Format("{0} = null;", variableName));

                }
            }

            if (referencedFile.LoadedOnlyWhenReferenced)
            {
                codeBlock = codeBlock.End();
            }
            AddEndIfIfNecessary(codeBlock, referencedFile);
            return codeBlock;
        }


        public static ICodeBlock GetAddToManagersForReferencedFile(IElement mSaveObject, ReferencedFileSave referencedFile)
        {
            bool shouldAddToManagers = GetIfShouldAddToManagers(mSaveObject, referencedFile);

            ICodeBlock codeBlock = new CodeDocument(3);

            // We don't want to add shared static stuff to the manager - it's just used to pull and clone objects out of.
            // Update September 12, 2012
            // We actually do want to add
            // static objects if they are part
            // of a Screen.
            if (shouldAddToManagers)
            {
                ICodeBlock currentBlock = codeBlock;

                AssetTypeInfo ati = referencedFile.GetAssetTypeInfo();
                string fileName = referencedFile.Name;

                AddIfConditionalSymbolIfNecesssary(codeBlock, referencedFile);

                string typeName = ati.RuntimeTypeName;
                string variableName = referencedFile.GetInstanceName();

                if (BaseElementTreeNode.IsOnOwnLayer(mSaveObject) && ati.LayeredAddToManagersMethod.Count != 0 && !string.IsNullOrEmpty(ati.LayeredAddToManagersMethod[0]))
                {
                    string layerAddToManagersMethod = ati.LayeredAddToManagersMethod[0];
                    if (mSaveObject is EntitySave)
                    {
                        layerAddToManagersMethod = layerAddToManagersMethod.Replace("mLayer", "layerToAddTo");
                    }

                    currentBlock.Line(layerAddToManagersMethod.Replace("this", variableName) + ";");
                }
                else if (ati.LayeredAddToManagersMethod.Count != 0 && !string.IsNullOrEmpty(ati.LayeredAddToManagersMethod[0]) && mSaveObject is ScreenSave)
                {
                    string layerAddToManagersMethod = ati.LayeredAddToManagersMethod[0];

                    // The Screen has an mLayer
                    //layerAddToManagersMethod = layerAddToManagersMethod.Replace("mLayer", "layerToAddTo");

                    currentBlock.Line(layerAddToManagersMethod.Replace("this", variableName) + ";");
                }
                else
                {
                    currentBlock.Line(ati.AddToManagersMethod[0].Replace("this", variableName) + ";");
                }

                if (referencedFile.IsManuallyUpdated && !string.IsNullOrEmpty(ati.MakeManuallyUpdatedMethod))
                {
                    currentBlock.Line(ati.MakeManuallyUpdatedMethod.Replace("this", variableName) + ";");
                }
                AddEndIfIfNecessary(codeBlock, referencedFile);
            }

            return codeBlock;
        }

        public static bool GetIfShouldAddToManagers(IElement saveObject, ReferencedFileSave referencedFile)
        {
            bool shouldAddToManagers = referencedFile.LoadedAtRuntime && !referencedFile.LoadedOnlyWhenReferenced;

            if (shouldAddToManagers)
            {
                AssetTypeInfo ati = referencedFile.GetAssetTypeInfo();

                // We don't want to add shared static stuff to the manager - it's just used to pull and clone objects out of.
                // Update September 12, 2012
                // We actually do want to add
                // static objects if they are part
                // of a Screen.
                shouldAddToManagers = ati != null &&
                    (!referencedFile.IsSharedStatic || saveObject is ScreenSave) &&
                    ati.AddToManagersMethod.Count != 0 && !string.IsNullOrEmpty(ati.AddToManagersMethod[0]) && referencedFile.AddToManagers;
            }
            return shouldAddToManagers;
        }



        public static void GenerateConvertToManuallyUpdated(ICodeBlock currentBlock, ReferencedFileSave rfs)
        {
            if (rfs.LoadedAtRuntime && !rfs.LoadedOnlyWhenReferenced)
            {
                AssetTypeInfo ati = rfs.GetAssetTypeInfo();

                if (!rfs.IsSharedStatic && ati != null && !string.IsNullOrEmpty(ati.MakeManuallyUpdatedMethod))
                {
                    AddIfConditionalSymbolIfNecesssary(currentBlock, rfs);
                    currentBlock.Line(ati.MakeManuallyUpdatedMethod.Replace("this", rfs.GetInstanceName()) + ";");
                    AddEndIfIfNecessary(currentBlock, rfs);
                }
            }
        }

        internal static void 
            GenerateGetStaticMemberMethod(List<ReferencedFileSave> rfsList, ICodeBlock codeBlock, bool isGlobalContent, bool inheritsFromElement)
        {
            string functionName = "GetStaticMember";

            GenerateGetFileMethodByName(rfsList, codeBlock, inheritsFromElement, functionName, true);

        }

        internal static void GenerateGetFileMethodByName(List<ReferencedFileSave> rfsList, ICodeBlock codeBlock, bool inheritsFromElement, string functionName, bool makeObsolete)
        {
            bool hasStaticMembers = false;

            foreach (ReferencedFileSave rfs in rfsList)
            {
                if (rfs.IsSharedStatic)
                {
                    hasStaticMembers = true;
                }
            }


            ICodeBlock currentBlock = codeBlock;
            if (makeObsolete)
            {
                currentBlock.Line("[System.Obsolete(\"Use GetFile instead\")]");
            }

            currentBlock = currentBlock
                .Function(functionName, "string memberName", Public: true, Static: true,
                            New: inheritsFromElement, Type: "object");
            if (hasStaticMembers)
            {
                currentBlock = currentBlock
                    .Switch("memberName");

                foreach (ReferencedFileSave rfs in rfsList)
                {

                    // Localization databases currently don't create members
                    if (rfs.IsSharedStatic && rfs.GetGeneratesMember())
                    {

                        string instanceName = rfs.GetInstanceName();

                        AddIfConditionalSymbolIfNecesssary(currentBlock, rfs);

                        currentBlock
                            .CaseNoBreak("\"" + instanceName + "\"")
                                .Line("return " + instanceName + ";");

                        AddEndIfIfNecessary(currentBlock, rfs);
                    }
                    //stringBuilder.AppendLine("\t\t\t\t\tbreak;");
                }

                currentBlock = currentBlock.End();
            }

            currentBlock.Line("return null;");
        }


        internal static void GenerateExceptionForPostInitializeLoads(ICodeBlock curBlock)
        {

            if (ProjectManager.GlueProjectSave.PerformanceSettingsSave.ThrowExceptionOnPostInitializeContentLoad)
            {
                // This used
                // to always be
                // generated, but
                // now we only generate
                // it if the setting is set
                // to true.  This elminates a
                // lot of unreachable code warnings.
                //stringBuilder.AppendLine(tabs + "const bool throwExceptionIfAfterInitialize = true;");
                //                curBlock.If("throwExceptionIfAfterInitialize && registerUnload && ScreenManager.CurrentScreen != null" +
                // Update:  Actually why do we even need registerUnload?
                //                curBlock.If("registerUnload && ScreenManager.CurrentScreen != null && FlatRedBall.FlatRedBallServices.IsThreadPrimary()" +
                // Update:  If this requires primary thread loading then we'll actually be doing
                // the "async" loading on the primary thread, so we shouldn't throw an exception.
                curBlock.Line("#if !REQUIRES_PRIMARY_THREAD_LOADING");

                curBlock.If("FlatRedBall.Screens.ScreenManager.CurrentScreen != null && FlatRedBall.FlatRedBallServices.IsThreadPrimary()" +
                    " && FlatRedBall.Screens.ScreenManager.CurrentScreen.ActivityCallCount > 0 && !FlatRedBall.Screens.ScreenManager.CurrentScreen.IsActivityFinished")
                            .Line("throw new System.InvalidOperationException(\"Content is being loaded after the current Screen is initialized.  " + 
                            "This exception is being thrown because of a setting in Glue.\");")
                            .End();
                curBlock.Line("#endif");
            }
        }
    }
}
