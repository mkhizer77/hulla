using Hulla.Presentation.Navigation;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Hulla.Presentation.Tests
{
    /// <summary>
    /// The scene references a build depends on. The screen tests load the catalog themselves, so a
    /// scene without one only fails on a device, with a black screen.
    /// </summary>
    public sealed class SceneWiringTests
    {
        [Test]
        public void TheMainSceneShellHasTheScreenCatalog()
        {
            var setup = EditorSceneManager.GetSceneManagerSetup();
            try
            {
                EditorSceneManager.OpenScene("Assets/Scenes/Main.unity", OpenSceneMode.Single);
                var shell = Object.FindAnyObjectByType<ShellView>();
                Assert.IsNotNull(shell, "Main has a ShellView");
                var catalog = new SerializedObject(shell).FindProperty("_catalog").objectReferenceValue as ScreenCatalog;
                Assert.IsNotNull(catalog, "ShellView._catalog is assigned in Main.unity");

                var fields = new SerializedObject(catalog!).GetIterator();
                fields.NextVisible(true);
                while (fields.NextVisible(false))
                {
                    if (fields.propertyType == SerializedPropertyType.ObjectReference && fields.name != "m_Script")
                    {
                        Assert.IsNotNull(fields.objectReferenceValue, "catalog entry " + fields.name);
                    }
                }
            }
            finally
            {
                if (setup.Length > 0)
                {
                    EditorSceneManager.RestoreSceneManagerSetup(setup);
                }
            }
        }
    }
}
