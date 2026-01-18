#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public class SoundCategoryBatchAdder : EditorWindow
{
    [SerializeField] private bool includeInactive = true;
    [SerializeField] private bool applyToOpenScenes = true;
    [SerializeField] private bool applyToAllPrefabs = true;

    [SerializeField] private SoundBus defaultBus = SoundBus.SFX;

    // (선택) 간단 규칙 기반 자동 분류
    [SerializeField] private bool autoGuessBusByName = true;
    [SerializeField] private string bgmKeyword = "BGM";
    [SerializeField] private string uiKeyword = "UI";
    [SerializeField] private string voiceKeyword = "Voice";

    [MenuItem("Tools/Audio/Batch Add SoundCategory")]
    public static void Open() => GetWindow<SoundCategoryBatchAdder>("Add SoundCategory");

    private void OnGUI()
    {
        EditorGUILayout.LabelField("Targets", EditorStyles.boldLabel);
        includeInactive = EditorGUILayout.Toggle("Include Inactive", includeInactive);
        applyToOpenScenes = EditorGUILayout.Toggle("Open Scenes", applyToOpenScenes);
        applyToAllPrefabs = EditorGUILayout.Toggle("All Prefabs (Project)", applyToAllPrefabs);

        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField("Default", EditorStyles.boldLabel);
        defaultBus = (SoundBus)EditorGUILayout.EnumPopup("Default Bus", defaultBus);

        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField("Auto Guess (Optional)", EditorStyles.boldLabel);
        autoGuessBusByName = EditorGUILayout.Toggle("Guess Bus By Name", autoGuessBusByName);

        using (new EditorGUI.DisabledScope(!autoGuessBusByName))
        {
            bgmKeyword = EditorGUILayout.TextField("BGM Keyword", bgmKeyword);
            uiKeyword = EditorGUILayout.TextField("UI Keyword", uiKeyword);
            voiceKeyword = EditorGUILayout.TextField("Voice Keyword", voiceKeyword);
        }

        EditorGUILayout.Space(12);

        if (GUILayout.Button("Add SoundCategory To All AudioSources"))
        {
            int added = 0;
            if (applyToOpenScenes) added += AddInOpenScenes();
            if (applyToAllPrefabs) added += AddInAllPrefabs();

            Debug.Log($"[SoundCategoryBatchAdder] Added SoundCategory: {added}");
        }

        EditorGUILayout.HelpBox(
            "AudioSource는 있는데 SoundCategory가 없는 오브젝트에만 컴포넌트를 추가합니다.\n" +
            "기본 bus는 Default Bus로 설정되며, Guess Bus By Name이 켜져 있으면 이름 키워드로 BGM/UI/Voice를 우선 추정합니다.",
            MessageType.Info);
    }

    private int AddInOpenScenes()
    {
        int added = 0;

        for (int i = 0; i < EditorSceneManager.sceneCount; i++)
        {
            var scene = EditorSceneManager.GetSceneAt(i);
            if (!scene.isLoaded) continue;

            foreach (var root in scene.GetRootGameObjects())
            {
                var sources = root.GetComponentsInChildren<AudioSource>(includeInactive);
                foreach (var src in sources)
                {
                    if (src == null) continue;
                    if (src.GetComponent<SoundCategory>() != null) continue;

                    Undo.RecordObject(src.gameObject, "Add SoundCategory");
                    var cat = Undo.AddComponent<SoundCategory>(src.gameObject);
                    cat.bus = GuessBus(src.gameObject.name);

                    EditorUtility.SetDirty(cat);
                    added++;
                }
            }

            EditorSceneManager.MarkSceneDirty(scene);
        }

        return added;
    }

    private int AddInAllPrefabs()
    {
        int added = 0;
        string[] prefabGuids = AssetDatabase.FindAssets("t:Prefab");

        try
        {
            AssetDatabase.StartAssetEditing();

            foreach (var guid in prefabGuids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var prefabRoot = PrefabUtility.LoadPrefabContents(path);

                bool changed = false;

                var sources = prefabRoot.GetComponentsInChildren<AudioSource>(includeInactive);
                foreach (var src in sources)
                {
                    if (src == null) continue;
                    if (src.GetComponent<SoundCategory>() != null) continue;

                    var cat = prefabRoot == null
                        ? src.gameObject.AddComponent<SoundCategory>()
                        : src.gameObject.AddComponent<SoundCategory>();

                    cat.bus = GuessBus(src.gameObject.name);

                    EditorUtility.SetDirty(cat);
                    changed = true;
                    added++;
                }

                if (changed)
                {
                    PrefabUtility.SaveAsPrefabAsset(prefabRoot, path);
                }

                PrefabUtility.UnloadPrefabContents(prefabRoot);
            }
        }
        finally
        {
            AssetDatabase.StopAssetEditing();
            AssetDatabase.Refresh();
        }

        return added;
    }

    private SoundBus GuessBus(string objectName)
    {
        if (!autoGuessBusByName) return defaultBus;

        // 우선순위: Voice > BGM > UI > Default
        // (원하면 UI를 SFX_UI로 보고 UI로 분리하는 식으로 수정 가능)
        if (!string.IsNullOrEmpty(voiceKeyword) && objectName.Contains(voiceKeyword))
            return SoundBus.Voice;

        if (!string.IsNullOrEmpty(bgmKeyword) && objectName.Contains(bgmKeyword))
            return SoundBus.BGM;

        if (!string.IsNullOrEmpty(uiKeyword) && objectName.Contains(uiKeyword))
            return SoundBus.UI;

        return defaultBus;
    }
}
#endif
