// Editor menu: GameObject > CRIX > Flappy Crix Arcade Machine (single player or linked).
// It also makes two materials with your render pipeline's shaders (Assets/CrixArcade/) and
// hands them to the cabinet, so the shaders are included when you build the game.
// Part of the Flappy Crix Arcade SDK - made with AI (Claude by Anthropic).

using UnityEditor;
using UnityEngine;

namespace CrixArcade.EditorTools
{
    static class ArcadeMenu
    {
        const string Folder = "Assets/CrixArcade";

        [MenuItem("GameObject/CRIX/Flappy Crix Arcade Machine", false, 10)]
        static void Single(MenuCommand cmd) => Create(cmd, "");

        [MenuItem("GameObject/CRIX/Flappy Crix Arcade Machine (linked for races)", false, 11)]
        static void Linked(MenuCommand cmd) => Create(cmd, "arcade");

        [MenuItem("GameObject/CRIX/Flappy Crix Arcade Link", false, 12)]
        static void Link(MenuCommand cmd)
        {
            if (Object.FindObjectOfType<ArcadeLink>() != null) { Selection.activeObject = Object.FindObjectOfType<ArcadeLink>(); return; }
            var go = new GameObject("Flappy Crix Arcade Link");
            go.AddComponent<ArcadeLink>();
            Undo.RegisterCreatedObjectUndo(go, "Create Arcade Link");
            Selection.activeObject = go;
        }

        static void Create(MenuCommand cmd, string group)
        {
            int n = Object.FindObjectsOfType<ArcadeMachine>().Length + 1;
            var go = new GameObject("Flappy Crix Arcade " + n);
            var parent = cmd.context as GameObject;
            if (parent != null) go.transform.SetParent(parent.transform, false);
            else if (SceneView.lastActiveSceneView != null)
            {
                // on the floor where the scene view is looking
                var piv = SceneView.lastActiveSceneView.pivot;
                go.transform.position = new Vector3(piv.x, 0, piv.z);
            }
            var m = go.AddComponent<ArcadeMachine>();
            m.cabinetId = "cabinet-" + n + "-" + System.Guid.NewGuid().ToString("N").Substring(0, 6);
            m.linkGroup = group;
            m.litMaterial = Mat("Arcade Lit", true);
            m.unlitMaterial = Mat("Arcade Glow", false);
            Undo.RegisterCreatedObjectUndo(go, "Create Flappy Crix Arcade");
            Selection.activeObject = go;
        }

        static Material Mat(string name, bool lit)
        {
            string path = Folder + "/" + name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m != null) return m;
            if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets", "CrixArcade");
            bool urp = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline != null;
            Shader sh = null;
            foreach (var s in lit
                ? (urp ? new[] { "Universal Render Pipeline/Lit", "Universal Render Pipeline/Simple Lit" } : new[] { "Standard", "Legacy Shaders/Diffuse" })
                : (urp ? new[] { "Universal Render Pipeline/Unlit" } : new[] { "Unlit/Texture" }))
                if ((sh = Shader.Find(s)) != null) break;
            if (sh == null) { Debug.LogWarning("[CrixArcade] No " + (lit ? "lit" : "unlit") + " shader found; the cabinet will pick one when it runs."); return null; }
            m = new Material(sh) { name = name };
            AssetDatabase.CreateAsset(m, path);
            AssetDatabase.SaveAssets();
            return m;
        }
    }

    [CustomEditor(typeof(ArcadeMachine))]
    sealed class ArcadeMachineEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            EditorGUILayout.HelpBox(
                "Faces its blue arrow (Z): the player stands there.\n" +
                "VR: put an ArcadeHand on each fingertip of your LOCAL player.\n" +
                "Races: same Link Group on cabinets that stand together, and give ArcadeLink a transport (Samples).",
                MessageType.Info);
            DrawDefaultInspector();
            var m = (ArcadeMachine)target;
            if (Application.isPlaying && m.Game != null)
                EditorGUILayout.LabelField("Now", m.Game.Screen + "  score " + m.Game.Score + (m.Game.Error != null ? "  ERROR " + m.Game.Error : ""));
        }
    }
}
