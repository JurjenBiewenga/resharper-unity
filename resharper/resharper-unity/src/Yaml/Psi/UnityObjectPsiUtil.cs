using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using JetBrains.Annotations;
using JetBrains.ProjectModel;
using JetBrains.ReSharper.Plugins.Unity.Yaml.Psi.Caches;
using JetBrains.ReSharper.Plugins.Unity.Yaml.Psi.Modules;
using JetBrains.ReSharper.Plugins.Yaml.Psi;
using JetBrains.ReSharper.Plugins.Yaml.Psi.Tree;
using JetBrains.ReSharper.Psi;
using JetBrains.ReSharper.Psi.Caches.Persistence;
using JetBrains.ReSharper.Psi.CSharp.Tree;
using JetBrains.ReSharper.Psi.Files;
using JetBrains.ReSharper.Psi.Tree;
using JetBrains.Util;
using JetBrains.Util.dataStructures;
using Lex;

namespace JetBrains.ReSharper.Plugins.Unity.Yaml.Psi
{
    public static class UnityObjectPsiUtil
    {
        [NotNull]
        public static string GetComponentName([NotNull] IYamlDocument componentDocument)
        {
            var name = componentDocument.GetUnityObjectPropertyValue(UnityYamlConstants.NameProperty).AsString();
            if (!string.IsNullOrWhiteSpace(name))
                return name;

            var scriptDocument = componentDocument.GetUnityObjectDocumentFromFileIDProperty(UnityYamlConstants.ScriptProperty);
            name = scriptDocument.GetUnityObjectPropertyValue(UnityYamlConstants.NameProperty).AsString();
            if (!string.IsNullOrWhiteSpace(name))
                return name;

            var fileID = componentDocument.GetUnityObjectPropertyValue(UnityYamlConstants.ScriptProperty).AsFileID();
            if (fileID != null && fileID.IsExternal && fileID.IsMonoScript)
            {
                var typeElement = GetTypeElementFromScriptAssetGuid(componentDocument.GetSolution(), fileID.guid);
                if (typeElement != null)
                {
                    // TODO: Format like in Unity, by splitting the camel humps
                    return typeElement.ShortName + " (Script)";
                }
            }

            return scriptDocument.GetUnityObjectTypeFromRootNode()
                   ?? componentDocument.GetUnityObjectTypeFromRootNode()
                   ?? "Component";
        }

        // Common method to process from startGameObject(or his component) to scene (or prefab) root elemment. Selector will be called for 
        // each visited game object
        public static void ProcessToRoot([CanBeNull] IYamlDocument startGameObject, Action<IYamlDocument, IBlockMappingNode> selector)
        {
            if (startGameObject == null)
                return;
            
            var solution = startGameObject.GetSolution();
            var yamlFileCache = solution.GetComponent<MetaFileGuidCache>();
            var externalFilesModuleFactory = solution.GetComponent<UnityExternalFilesModuleFactory>();

            ProcessPrefabFromToRoot(yamlFileCache, externalFilesModuleFactory, startGameObject, null, selector);
        }

        private static void ProcessPrefabFromToRoot(MetaFileGuidCache yamlFileCache, UnityExternalFilesModuleFactory externalFilesModuleFactory,
            IYamlDocument startGameObject, IBlockMappingNode modifications, Action<IYamlDocument, IBlockMappingNode> selector)
        {
            // We can start traverse scene (or prefab) hierarchy from component/game object
            // Each component has game object, only transform component know about parent
            // If game object or component has m_CorrespondingSourceObject which is not zero, it means that all data stored in
            // separated file (which is called prefab). All mofications for prefab is stored in current document in yaml document which can be found by
            // m_PrefabInstance fileId
            // So, if we meat component, we should check it for m_CorrespondingSourceObject. If we found it, we will traverse .prefab file and apply 
            // modifications from m_PrefabInstance, then return to our file and continue traverse from parent of prefab
            // If we meat component without m_CorrespondingSourceObject, we take it "m_GameObject", send to selector, then travers from parent which 
            // is stored in transform for this game object
            // If we meat game object, we send it to selector, then find transform and find parent via his transform and continue traverse.
            // If parent can not be found, it means that we meat root node.
            
            
            if (startGameObject == null)
                return;
            var currentGameObject = startGameObject;
            while (currentGameObject != null)
            {
                var correspondingId = currentGameObject.GetUnityObjectPropertyValue(UnityYamlConstants.CorrespondingSourceObjectProperty)?.AsFileID();
                var prefabInstanceId = currentGameObject.GetUnityObjectPropertyValue(UnityYamlConstants.PrefabInstanceProperty)?.AsFileID();
                if (correspondingId != null && correspondingId != FileID.Null)
                {
                    // This should never happen, but data can be corrupted
                    if (prefabInstanceId == null || prefabInstanceId == FileID.Null)
                        return;

                    var file = (IYamlFile) currentGameObject.GetContainingFile();
                    var prefabInstance = file.FindDocumentByAnchor(prefabInstanceId.fileID);
                    
                    var prefabSourceFile = yamlFileCache.GetAssetFilePathsFromGuid(correspondingId.guid);
                    if (prefabSourceFile.Count > 1 || prefabSourceFile.Count == 0) 
                        return;

                    // Is prefab file committed???
                    externalFilesModuleFactory.PsiModule.NotNull("externalFilesModuleFactory.PsiModule != null")
                        .TryGetFileByPath(prefabSourceFile.First(), out var sourceFile);
                    
                    if (sourceFile == null)
                        return;

                    var prefabFile = (IYamlFile)sourceFile.GetDominantPsiFile<YamlLanguage>();
                    
                    var prefabSourceObject = prefabFile.FindDocumentByAnchor(correspondingId.fileID); // It can be component, game object or prefab
                    // if it component we should query game object. 
                    var prefabStartGameObject = prefabSourceObject.GetUnityObjectDocumentFromFileIDProperty(UnityYamlConstants.GameObjectProperty) ?? prefabSourceObject;

                    var localModifications = GetPrefabModification(prefabInstance);
                    ProcessPrefabFromToRoot(yamlFileCache, externalFilesModuleFactory, prefabStartGameObject, localModifications, selector);
                    currentGameObject = GetTransformFromPrefab(prefabInstance);
                }
                else
                {
                    
                    selector(currentGameObject.GetUnityObjectDocumentFromFileIDProperty(UnityYamlConstants.GameObjectProperty) ?? currentGameObject, modifications);
                    currentGameObject = currentGameObject.GetUnityObjectDocumentFromFileIDProperty(UnityYamlConstants.FatherProperty) 
                                        ?? FindTransformComponentForGameObject(currentGameObject).GetUnityObjectDocumentFromFileIDProperty(UnityYamlConstants.FatherProperty);
                }
            }
        }


        /// <summary>
        /// This method return path from component's owner to scene or prefab hierarachy root
        /// </summary>
        /// <param name="componentDocument">GameObject's component</param>
        /// <returns></returns>
        [NotNull]
        public static string GetGameObjectPathFromComponent([NotNull] IYamlDocument componentDocument)
        {
            var gameObjectDocument = componentDocument.GetUnityObjectDocumentFromFileIDProperty(UnityYamlConstants.GameObjectProperty) ?? componentDocument;

            var parts = new FrugalLocalList<string>();
            ProcessToRoot(gameObjectDocument, (document, modification) =>
            {
                string name = null;
                if (modification != null)
                {
                    var documentId = document.GetFileId();
                    name = GetValueFromModifications(modification, documentId, UnityYamlConstants.NameProperty);
                }
                if (name == null)
                {
                    name = document.GetUnityObjectPropertyValue(UnityYamlConstants.NameProperty).AsString();
                }

                if (name?.Equals(string.Empty) == true)
                    name = null;
                parts.Add(name ?? "INVALID");
            });

            if (parts.Count == 1)
                return parts[0];

            var sb = new StringBuilder();
            for (var i = parts.Count - 1; i >= 0; i--)
            {
                sb.Append(parts[i]);
                sb.Append("\\");
            }

            return sb.ToString();
        }

        private static IBlockMappingNode GetPrefabModification(IYamlDocument yamlDocument)
        {
            // Prefab instance has a map of modifications, that stores delta of instance and prefab
            return yamlDocument.GetUnityObjectPropertyValue(UnityYamlConstants.ModificationProperty) as IBlockMappingNode;
        }

        private static IYamlDocument GetTransformFromPrefab(IYamlDocument prefabInstanceDocument)
        {
            // Prefab instance stores it's father in modification map
            var prefabModification = GetPrefabModification(prefabInstanceDocument);

            var fileID = prefabModification?.FindMapEntryBySimpleKey(UnityYamlConstants.TransformParentProperty)?.Value.AsFileID();
            if (fileID == null)
                return null;

            var file = (IYamlFile) prefabInstanceDocument.GetContainingFile();
            return file.FindDocumentByAnchor(fileID.fileID);
        }

        [CanBeNull]
        public static ITypeElement GetTypeElementFromScriptAssetGuid(ISolution solution, [CanBeNull] string assetGuid)
        {
            if (assetGuid == null)
                return null;

            var cache = solution.GetComponent<MetaFileGuidCache>();
            var assetPaths = cache.GetAssetFilePathsFromGuid(assetGuid);
            if (assetPaths == null || assetPaths.IsEmpty())
                return null;

            // TODO: Multiple candidates!
            // I.e. someone has copy/pasted a .meta file
            if (assetPaths.Count != 1)
                return null;

            var projectItems = solution.FindProjectItemsByLocation(assetPaths[0]);
            var assetFile = projectItems.FirstOrDefault() as IProjectFile;
            if (!(assetFile?.GetPrimaryPsiFile() is ICSharpFile csharpFile))
                return null;

            var expectedClassName = assetPaths[0].NameWithoutExtension;
            var psiSourceFile = csharpFile.GetSourceFile();
            if (psiSourceFile == null)
                return null;

            var psiServices = csharpFile.GetPsiServices();
            var elements = psiServices.Symbols.GetTypesAndNamespacesInFile(psiSourceFile);
            foreach (var element in elements)
            {
                // Note that theoretically, there could be multiple classes with the same name in different namespaces.
                // Unity's own behaviour here is undefined - it arbitrarily chooses one
                // TODO: Multiple candidates in a file
                if (element is ITypeElement typeElement && typeElement.ShortName == expectedClassName)
                    return typeElement;
            }

            return null;
        }

        [CanBeNull]
        public static IYamlDocument FindTransformComponentForGameObject([CanBeNull] IYamlDocument gameObjectDocument)
        {
            // GameObject:
            //   m_Component:
            //   - component: {fileID: 1234567890}
            //   - component: {fileID: 1234567890}
            //   - component: {fileID: 1234567890}
            // One of these components is the RectTransform(GUI, 2D) or Transform(3D). Most likely the first, but we can't rely on order
            if (gameObjectDocument?.GetUnityObjectPropertyValue("m_Component") is IBlockSequenceNode components)
            {
                var file = (IYamlFile) gameObjectDocument.GetContainingFile();

                foreach (var componentEntry in components.EntriesEnumerable)
                {
                    // - component: {fileID: 1234567890}
                    var componentNode = componentEntry.Value as IBlockMappingNode;
                    var componentFileID = componentNode?.EntriesEnumerable.FirstOrDefault()?.Value.AsFileID();
                    if (componentFileID != null && !componentFileID.IsNullReference && !componentFileID.IsExternal)
                    {
                        var component = file.FindDocumentByAnchor(componentFileID.fileID);
                        var componentName = component.GetUnityObjectTypeFromRootNode();
                        if (componentName != null && (componentName.Equals(UnityYamlConstants.RectTransformComponent) || componentName.Equals(UnityYamlConstants.TransformComponent)))
                            return component;
                    }
                }
            }

            return null;
        }

        public static string GetValueFromModifications(IBlockMappingNode modification, string targetFileId, string value)
        {
            if (targetFileId != null && modification.FindMapEntryBySimpleKey(UnityYamlConstants.ModificationsProperty)?.Value is IBlockSequenceNode modifications)
            {
                foreach (var element in modifications.Entries)
                {
                    if (!(element.Value is IBlockMappingNode mod))
                        return null;
                    var type = (mod.FindMapEntryBySimpleKey(UnityYamlConstants.PropertyPathProperty)?.Value as IPlainScalarNode)
                        ?.Text.GetText();
                    var target = mod.FindMapEntryBySimpleKey(UnityYamlConstants.TargetProperty)?.Value?.AsFileID();
                    if (type?.Equals(value) == true && target?.fileID.Equals(targetFileId) == true)
                    {
                        return (mod.FindMapEntryBySimpleKey(UnityYamlConstants.ValueProperty)?.Value as IPlainScalarNode)?.Text.GetText();
                    }
                }
            }

            return null;
        }
    }
}