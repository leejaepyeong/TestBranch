#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.SceneManagement;

public class AudioAuditAndFixWindow : EditorWindow
{
    [Serializable]
    private class Row
    {
        public bool selected;

        public string kind;          // Prefab / Scene
        public string assetPath;     // prefab or scene path
        public string hierarchyPath; // Root/Child/...

        public int sceneInstanceId;  // Scene object 식별용 (Scene일 때만 유효)

        public string goName;

        public string clipName;
        public string clipPath;

        public bool hasSoundCategory;
        public string soundBus;

        public bool hasMixerGroup;
        public string mixerGroupName;
        public string mixerAssetPath;

        public bool is3D;
        public float spatialBlend;
    }

    private Vector2 _scroll;
    private List<Row> _rows = new List<Row>();
    private List<Row> _filtered = new List<Row>();

    // Scan targets
    private bool _scanPrefabs = true;
    private bool _scanOpenScenes = true;
    private bool _scanBuildScenes = false;
    private bool _includeInactive = true;

    // Filters
    private string _search = "";
    private bool _onlyMissingCategory = false;
    private bool _onlyMissingMixer = false;
    private int _busFilterIndex = 0; // 0:All, 1:BGM, 2:SFX, 3:UI, 4:Voice, 5:(None)
    private readonly string[] _busFilterLabels = { "All", "BGM", "SFX", "UI", "Voice", "(No Category)" };

    // Fix actions
    private SoundBus _setBusValue = SoundBus.SFX;

    private AudioMixer _mixer;
    private string _bgmGroupName = "BGM";
    private string _bgmMapGroupName = "BGM_MAP";
    private string _bgmDialogGroupName = "BGM_DIALOG";
    private string _sfxGroupName = "SFX";
    private string _uiGroupName = "SFX_UI";
    private string _voiceGroupName = "Voice";

    private const int MAX_PREVIEW = 5000;

    [MenuItem("Tools/Audio/Audio Audit & Fix")]
    public static void Open() => GetWindow<AudioAuditAndFixWindow>("Audio Audit & Fix");

    private void OnGUI()
    {
        DrawScanTargets();

        EditorGUILayout.Space(6);
        DrawTopButtons();

        EditorGUILayout.Space(10);
        DrawFilters();

        EditorGUILayout.Space(8);
        DrawSelectionButtons();

        EditorGUILayout.Space(10);
        DrawFixPanel();

        EditorGUILayout.Space(12);
        DrawTableHeader();

        _scroll = EditorGUILayout.BeginScrollView(_scroll);
        int shown = Mathf.Min(_filtered.Count, MAX_PREVIEW);
        for (int i = 0; i < shown; i++)
            DrawRow(_filtered[i], i);

        if (_filtered.Count > MAX_PREVIEW)
            EditorGUILayout.HelpBox($"표시가 너무 많아 상위 {MAX_PREVIEW}개만 표시합니다. 검색/필터로 줄여주세요.", MessageType.Warning);

        EditorGUILayout.EndScrollView();
    }

    private void DrawScanTargets()
    {
        EditorGUILayout.LabelField("Scan Targets", EditorStyles.boldLabel);
        using (new EditorGUILayout.HorizontalScope())
        {
            _scanPrefabs = EditorGUILayout.ToggleLeft("Prefabs", _scanPrefabs, GUILayout.Width(120));
            _scanOpenScenes = EditorGUILayout.ToggleLeft("Open Scenes", _scanOpenScenes, GUILayout.Width(120));
            _scanBuildScenes = EditorGUILayout.ToggleLeft("Build Scenes (open & scan)", _scanBuildScenes, GUILayout.Width(200));
            _includeInactive = EditorGUILayout.ToggleLeft("Include Inactive", _includeInactive, GUILayout.Width(150));
        }
    }

    private void DrawTopButtons()
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Scan", GUILayout.Height(26)))
            {
                Scan();
                ApplyFilters();
            }

            if (GUILayout.Button("Export CSV", GUILayout.Height(26)))
                ExportCsv();

            GUILayout.FlexibleSpace();
            EditorGUILayout.LabelField($"Total: {_rows.Count}   Shown: {_filtered.Count}", GUILayout.Width(240));
        }
    }

    private void DrawFilters()
    {
        EditorGUILayout.LabelField("Filters", EditorStyles.boldLabel);

        using (new EditorGUILayout.HorizontalScope())
        {
            _search = EditorGUILayout.TextField("Search", _search);
            _busFilterIndex = EditorGUILayout.Popup("Bus", _busFilterIndex, _busFilterLabels, GUILayout.Width(260));
        }

        using (new EditorGUILayout.HorizontalScope())
        {
            _onlyMissingCategory = EditorGUILayout.ToggleLeft("Only Missing SoundCategory", _onlyMissingCategory, GUILayout.Width(240));
            _onlyMissingMixer = EditorGUILayout.ToggleLeft("Only Missing MixerGroup", _onlyMissingMixer, GUILayout.Width(210));

            if (GUILayout.Button("Apply", GUILayout.Width(80)))
                ApplyFilters();
        }
    }

    private void DrawSelectionButtons()
    {
        EditorGUILayout.LabelField("Selection", EditorStyles.boldLabel);
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Select All Shown", GUILayout.Width(140)))
                SetSelectedForFiltered(true);

            if (GUILayout.Button("Select None", GUILayout.Width(110)))
                SetSelectedForFiltered(false);

            if (GUILayout.Button("Select Missing Category", GUILayout.Width(180)))
                SelectFilteredWhere(r => !r.hasSoundCategory);

            if (GUILayout.Button("Select Missing Mixer", GUILayout.Width(160)))
                SelectFilteredWhere(r => !r.hasMixerGroup);

            GUILayout.FlexibleSpace();
            GUILayout.Label($"Selected: {CountSelected()}", GUILayout.Width(120));
        }
    }

    private void DrawFixPanel()
    {
        EditorGUILayout.LabelField("Fix Actions", EditorStyles.boldLabel);

        // 1) Add SoundCategory
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            EditorGUILayout.LabelField("SoundCategory", EditorStyles.boldLabel);

            using (new EditorGUILayout.HorizontalScope())
            {
                _setBusValue = (SoundBus)EditorGUILayout.EnumPopup("Bus Value", _setBusValue);

                if (GUILayout.Button("Add SoundCategory (Selected)", GUILayout.Width(230)))
                {
                    int changed = AddSoundCategoryToSelected(_setBusValue);
                    Debug.Log($"[AudioAudit&Fix] Added SoundCategory: {changed}");
                    Scan(); ApplyFilters();
                }

                if (GUILayout.Button("Set Bus (Selected)", GUILayout.Width(170)))
                {
                    int changed = SetBusToSelected(_setBusValue);
                    Debug.Log($"[AudioAudit&Fix] Updated bus: {changed}");
                    Scan(); ApplyFilters();
                }
            }
        }

        // 2) Assign Mixer Group
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            EditorGUILayout.LabelField("Assign MixerGroup By Bus", EditorStyles.boldLabel);

            _mixer = (AudioMixer)EditorGUILayout.ObjectField("Audio Mixer", _mixer, typeof(AudioMixer), false);

            using (new EditorGUILayout.HorizontalScope())
            {
                _bgmGroupName = EditorGUILayout.TextField("BGM", _bgmGroupName);
                _bgmMapGroupName = EditorGUILayout.TextField("BGM_MAP", _bgmMapGroupName);
                _bgmDialogGroupName = EditorGUILayout.TextField("BGM_DIALOG", _bgmDialogGroupName);
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                _sfxGroupName = EditorGUILayout.TextField("SFX", _sfxGroupName);
                _uiGroupName = EditorGUILayout.TextField("UI", _uiGroupName);
                _voiceGroupName = EditorGUILayout.TextField("Voice", _voiceGroupName);
            }

            using (new EditorGUI.DisabledScope(_mixer == null))
            {
                if (GUILayout.Button("Assign outputAudioMixerGroup (Selected)", GUILayout.Height(26)))
                {
                    int changed = AssignMixerGroupToSelected();
                    Debug.Log($"[AudioAudit&Fix] Assigned mixer group: {changed}");
                    Scan(); ApplyFilters();
                }
            }

            EditorGUILayout.HelpBox(
                "주의: Prefab 내부에 이름이 같은 오브젝트가 여러 개면 HierarchyPath로 찾을 때 첫 번째 매칭을 수정할 수 있습니다.\n" +
                "중복 이름이 많다면(예: 'AudioSource'가 반복) 이름 규칙을 조금 정리하거나, 필요하면 GUID 기반으로 확장하는게 안전합니다.",
                MessageType.Info);
        }
    }

    private void DrawTableHeader()
    {
        using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
        {
            GUILayout.Label("", GUILayout.Width(22));          // checkbox
            GUILayout.Label("#", GUILayout.Width(32));
            GUILayout.Label("Kind", GUILayout.Width(58));
            GUILayout.Label("Asset Path", GUILayout.Width(320));
            GUILayout.Label("Hierarchy", GUILayout.Width(360));
            GUILayout.Label("Clip", GUILayout.Width(180));
            GUILayout.Label("SoundCategory", GUILayout.Width(120));
            GUILayout.Label("MixerGroup", GUILayout.Width(170));
            GUILayout.Label("Mixer Asset", GUILayout.Width(260));
            GUILayout.Label("3D", GUILayout.Width(34));
            GUILayout.Label("", GUILayout.Width(110));
        }
    }

    private void DrawRow(Row r, int index)
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            r.selected = GUILayout.Toggle(r.selected, "", GUILayout.Width(22));
            GUILayout.Label((index + 1).ToString(), GUILayout.Width(32));
            GUILayout.Label(r.kind, GUILayout.Width(58));

            using (new EditorGUILayout.HorizontalScope(GUILayout.Width(320)))
            {
                GUILayout.Label(Truncate(r.assetPath, 52), GUILayout.Width(250));
                if (GUILayout.Button("Ping", GUILayout.Width(58)))
                    PingAsset(r.assetPath);
            }

            GUILayout.Label(Truncate(r.hierarchyPath, 58), GUILayout.Width(360));

            using (new EditorGUILayout.HorizontalScope(GUILayout.Width(180)))
            {
                GUILayout.Label(Truncate(r.clipName, 20), GUILayout.Width(120));
                if (!string.IsNullOrEmpty(r.clipPath) && GUILayout.Button("Clip", GUILayout.Width(54)))
                    PingAsset(r.clipPath);
            }

            GUILayout.Label(r.hasSoundCategory ? r.soundBus : "(None)", GUILayout.Width(120));
            GUILayout.Label(r.hasMixerGroup ? r.mixerGroupName : "(None)", GUILayout.Width(170));

            using (new EditorGUILayout.HorizontalScope(GUILayout.Width(260)))
            {
                GUILayout.Label(Truncate(r.mixerAssetPath, 40), GUILayout.Width(190));
                if (!string.IsNullOrEmpty(r.mixerAssetPath) && GUILayout.Button("Mixer", GUILayout.Width(60)))
                    PingAsset(r.mixerAssetPath);
            }

            GUILayout.Label(r.is3D ? "Y" : "N", GUILayout.Width(34));

            using (new EditorGUILayout.HorizontalScope(GUILayout.Width(110)))
            {
                if (GUILayout.Button("Copy Path", GUILayout.Width(80)))
                    EditorGUIUtility.systemCopyBuffer = $"{r.assetPath} :: {r.hierarchyPath}";
            }
        }

        var rect = GUILayoutUtility.GetRect(1, 1, GUILayout.ExpandWidth(true));
        EditorGUI.DrawRect(rect, new Color(0, 0, 0, 0.12f));
    }

    // -------------------- SCAN --------------------

    private void Scan()
    {
        _rows.Clear();

        if (_scanPrefabs) ScanAllPrefabs();
        if (_scanOpenScenes) ScanOpenScenes();
        if (_scanBuildScenes) ScanBuildScenes();

        // Dedup
        _rows = _rows
            .GroupBy(r => $"{r.kind}|{r.assetPath}|{r.hierarchyPath}")
            .Select(g => g.First())
            .ToList();
    }

    private void ScanAllPrefabs()
    {
        string[] guids = AssetDatabase.FindAssets("t:Prefab");
        foreach (var guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var sources = root.GetComponentsInChildren<AudioSource>(_includeInactive);
                foreach (var src in sources)
                    AddRowFromAudioSource("Prefab", path, src.gameObject, src, sceneInstanceId: 0);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }
    }

    private void ScanOpenScenes()
    {
        for (int i = 0; i < EditorSceneManager.sceneCount; i++)
        {
            var scene = EditorSceneManager.GetSceneAt(i);
            if (!scene.isLoaded) continue;

            foreach (var root in scene.GetRootGameObjects())
            {
                var sources = root.GetComponentsInChildren<AudioSource>(_includeInactive);
                foreach (var src in sources)
                    AddRowFromAudioSource("Scene", scene.path, src.gameObject, src, src.gameObject.GetInstanceID());
            }
        }
    }

    private void ScanBuildScenes()
    {
        var setup = EditorSceneManager.GetSceneManagerSetup();
        try
        {
            var buildScenes = EditorBuildSettings.scenes.Where(s => s.enabled && !string.IsNullOrEmpty(s.path)).ToList();
            foreach (var bs in buildScenes)
            {
                var scene = EditorSceneManager.OpenScene(bs.path, OpenSceneMode.Additive);
                foreach (var root in scene.GetRootGameObjects())
                {
                    var sources = root.GetComponentsInChildren<AudioSource>(_includeInactive);
                    foreach (var src in sources)
                        AddRowFromAudioSource("Scene", bs.path, src.gameObject, src, src.gameObject.GetInstanceID());
                }
                EditorSceneManager.CloseScene(scene, true);
            }
        }
        finally
        {
            EditorSceneManager.RestoreSceneManagerSetup(setup);
        }
    }

    private void AddRowFromAudioSource(string kind, string assetPath, GameObject go, AudioSource src, int sceneInstanceId)
    {
        var row = new Row();
        row.selected = false;

        row.kind = kind;
        row.assetPath = assetPath;
        row.goName = go.name;
        row.hierarchyPath = BuildHierarchyPath(go);
        row.sceneInstanceId = sceneInstanceId;

        var clip = src.clip;
        row.clipName = clip ? clip.name : "(None)";
        row.clipPath = clip ? AssetDatabase.GetAssetPath(clip) : "";

        var cat = go.GetComponent<SoundCategory>();
        row.hasSoundCategory = cat != null;
        row.soundBus = cat != null ? cat.bus.ToString() : "";

        var group = src.outputAudioMixerGroup;
        row.hasMixerGroup = group != null;
        row.mixerGroupName = group != null ? group.name : "";
        row.mixerAssetPath = group != null ? AssetDatabase.GetAssetPath(group.audioMixer) : "";

        row.spatialBlend = src.spatialBlend;
        row.is3D = src.spatialBlend > 0.001f;

        _rows.Add(row);
    }

    // -------------------- FILTER --------------------

    private void ApplyFilters()
    {
        IEnumerable<Row> q = _rows;

        if (_onlyMissingCategory) q = q.Where(r => !r.hasSoundCategory);
        if (_onlyMissingMixer) q = q.Where(r => !r.hasMixerGroup);

        if (_busFilterIndex == 5)
            q = q.Where(r => !r.hasSoundCategory);
        else if (_busFilterIndex > 0)
        {
            string bus = _busFilterLabels[_busFilterIndex];
            q = q.Where(r => r.hasSoundCategory && r.soundBus == bus);
        }

        if (!string.IsNullOrWhiteSpace(_search))
        {
            string s = _search.Trim();
            q = q.Where(r =>
                Contains(r.assetPath, s) ||
                Contains(r.hierarchyPath, s) ||
                Contains(r.goName, s) ||
                Contains(r.clipName, s) ||
                Contains(r.clipPath, s) ||
                Contains(r.mixerGroupName, s) ||
                Contains(r.mixerAssetPath, s) ||
                Contains(r.soundBus, s)
            );
        }

        _filtered = q.ToList();
        Repaint();
    }

    // -------------------- SELECTION --------------------

    private void SetSelectedForFiltered(bool value)
    {
        foreach (var r in _filtered)
            r.selected = value;
        Repaint();
    }

    private void SelectFilteredWhere(Func<Row, bool> predicate)
    {
        foreach (var r in _filtered)
            r.selected = predicate(r);
        Repaint();
    }

    private int CountSelected()
        => _rows.Count(r => r.selected);

    private List<Row> GetSelectedRows()
        => _rows.Where(r => r.selected).ToList();

    // -------------------- FIX: SoundCategory --------------------

    private int AddSoundCategoryToSelected(SoundBus busValue)
    {
        int changed = 0;
        var selected = GetSelectedRows();

        // Scene
        foreach (var row in selected.Where(r => r.kind == "Scene"))
        {
            var go = EditorUtility.InstanceIDToObject(row.sceneInstanceId) as GameObject;
            if (!go) continue;

            var src = go.GetComponent<AudioSource>();
            if (!src) continue;

            if (go.GetComponent<SoundCategory>() != null) continue;

            Undo.RecordObject(go, "Add SoundCategory");
            var cat = Undo.AddComponent<SoundCategory>(go);
            cat.bus = busValue;
            EditorUtility.SetDirty(go);

            var scene = go.scene;
            if (scene.IsValid())
                EditorSceneManager.MarkSceneDirty(scene);

            changed++;
        }

        // Prefab
        changed += ModifyPrefabs(selected.Where(r => r.kind == "Prefab"),
            (root, targetGo) =>
            {
                if (!targetGo) return false;
                if (!targetGo.GetComponent<AudioSource>()) return false;
                if (targetGo.GetComponent<SoundCategory>() != null) return false;

                var cat = targetGo.AddComponent<SoundCategory>();
                cat.bus = busValue;
                EditorUtility.SetDirty(targetGo);
                return true;
            });

        return changed;
    }

    private int SetBusToSelected(SoundBus busValue)
    {
        int changed = 0;
        var selected = GetSelectedRows();

        // Scene
        foreach (var row in selected.Where(r => r.kind == "Scene"))
        {
            var go = EditorUtility.InstanceIDToObject(row.sceneInstanceId) as GameObject;
            if (!go) continue;

            var cat = go.GetComponent<SoundCategory>();
            if (!cat) continue;

            if (cat.bus == busValue) continue;

            Undo.RecordObject(cat, "Set SoundBus");
            cat.bus = busValue;
            EditorUtility.SetDirty(cat);

            var scene = go.scene;
            if (scene.IsValid())
                EditorSceneManager.MarkSceneDirty(scene);

            changed++;
        }

        // Prefab
        changed += ModifyPrefabs(selected.Where(r => r.kind == "Prefab"),
            (root, targetGo) =>
            {
                if (!targetGo) return false;
                var cat = targetGo.GetComponent<SoundCategory>();
                if (!cat) return false;
                if (cat.bus == busValue) return false;

                cat.bus = busValue;
                EditorUtility.SetDirty(cat);
                return true;
            });

        return changed;
    }

    // -------------------- FIX: Mixer Assign --------------------

    private int AssignMixerGroupToSelected()
    {
        int changed = 0;
        var selected = GetSelectedRows();

        var map = BuildMixerGroupMap();
        if (map == null) return 0;

        // Scene
        foreach (var row in selected.Where(r => r.kind == "Scene"))
        {
            var go = EditorUtility.InstanceIDToObject(row.sceneInstanceId) as GameObject;
            if (!go) continue;

            var src = go.GetComponent<AudioSource>();
            if (!src) continue;

            var cat = go.GetComponent<SoundCategory>();
            if (!cat) continue;

            if (!map.TryGetValue(cat.bus, out var targetGroup) || targetGroup == null)
                continue;

            if (src.outputAudioMixerGroup == targetGroup)
                continue;

            Undo.RecordObject(src, "Assign AudioMixerGroup");
            src.outputAudioMixerGroup = targetGroup;
            EditorUtility.SetDirty(src);

            var scene = go.scene;
            if (scene.IsValid())
                EditorSceneManager.MarkSceneDirty(scene);

            changed++;
        }

        // Prefab
        changed += ModifyPrefabs(selected.Where(r => r.kind == "Prefab"),
            (root, targetGo) =>
            {
                if (!targetGo) return false;

                var src = targetGo.GetComponent<AudioSource>();
                if (!src) return false;

                var cat = targetGo.GetComponent<SoundCategory>();
                if (!cat) return false;

                if (!map.TryGetValue(cat.bus, out var targetGroup) || targetGroup == null)
                    return false;

                if (src.outputAudioMixerGroup == targetGroup)
                    return false;

                src.outputAudioMixerGroup = targetGroup;
                EditorUtility.SetDirty(src);
                return true;
            });

        return changed;
    }

    private Dictionary<SoundBus, AudioMixerGroup> BuildMixerGroupMap()
    {
        if (_mixer == null)
        {
            EditorUtility.DisplayDialog("Audio Audit & Fix", "AudioMixer를 지정해주세요.", "OK");
            return null;
        }

        AudioMixerGroup FindExact(string groupName)
        {
            var groups = _mixer.FindMatchingGroups(groupName);
            foreach (var g in groups)
                if (g != null && g.name == groupName) return g;
            return (groups != null && groups.Length > 0) ? groups[0] : null;
        }

        var map = new Dictionary<SoundBus, AudioMixerGroup>
        {
            { SoundBus.BGM,   FindExact(_bgmGroupName) },
            { SoundBus.BGM_MAP,   FindExact(_bgmMapGroupName) },
            { SoundBus.BGM_DIALOG,   FindExact(_bgmDialogGroupName) },
            { SoundBus.SFX,   FindExact(_sfxGroupName) },
            { SoundBus.UI,    FindExact(_uiGroupName) },
            { SoundBus.Voice, FindExact(_voiceGroupName) },
        };

        // 누락 경고
        foreach (var kv in map)
        {
            if (kv.Value == null)
                Debug.LogError($"[AudioAudit&Fix] MixerGroup not found for {kv.Key} (name: {GetGroupNameForBus(kv.Key)})");
        }

        return map;

        string GetGroupNameForBus(SoundBus bus) =>
            bus switch
            {
                SoundBus.BGM => _bgmGroupName,
                SoundBus.BGM_MAP => _bgmMapGroupName,
                SoundBus.BGM_DIALOG => _bgmDialogGroupName,
                SoundBus.SFX => _sfxGroupName,
                SoundBus.UI => _uiGroupName,
                SoundBus.Voice => _voiceGroupName,
                _ => ""
            };
    }

    // -------------------- PREFAB MOD HELPERS --------------------

    private int ModifyPrefabs(IEnumerable<Row> prefabRows, Func<GameObject, GameObject, bool> modifier)
    {
        int changedCount = 0;

        // 같은 prefabPath에 대한 로우를 묶어서 한번만 로드/세이브
        var groups = prefabRows.GroupBy(r => r.assetPath).ToList();

        try
        {
            AssetDatabase.StartAssetEditing();

            foreach (var g in groups)
            {
                string prefabPath = g.Key;
                var root = PrefabUtility.LoadPrefabContents(prefabPath);
                bool changed = false;

                foreach (var row in g)
                {
                    var target = FindByHierarchyPath(root, row.hierarchyPath);
                    if (target == null) continue;

                    if (modifier(root, target))
                    {
                        changed = true;
                        changedCount++;
                    }
                }

                if (changed)
                    PrefabUtility.SaveAsPrefabAsset(root, prefabPath);

                PrefabUtility.UnloadPrefabContents(root);
            }
        }
        finally
        {
            AssetDatabase.StopAssetEditing();
            AssetDatabase.Refresh();
        }

        return changedCount;
    }

    private static GameObject FindByHierarchyPath(GameObject prefabRoot, string hierarchyPath)
    {
        if (prefabRoot == null || string.IsNullOrEmpty(hierarchyPath))
            return null;

        var parts = hierarchyPath.Split('/');
        if (parts.Length == 0) return null;

        Transform t = prefabRoot.transform;

        // 첫 번째 파트는 root 이름일 가능성이 높음
        int start = 0;
        if (parts[0] == t.name) start = 1;

        for (int i = start; i < parts.Length; i++)
        {
            string name = parts[i];
            Transform next = null;

            // 같은 이름이 여러 개면 첫 번째 매칭
            for (int c = 0; c < t.childCount; c++)
            {
                var child = t.GetChild(c);
                if (child.name == name)
                {
                    next = child;
                    break;
                }
            }

            if (next == null) return null;
            t = next;
        }

        return t.gameObject;
    }

    // -------------------- CSV --------------------

    private void ExportCsv()
    {
        if (_rows == null || _rows.Count == 0)
        {
            EditorUtility.DisplayDialog("Audio Audit & Fix", "먼저 Scan을 실행해주세요.", "OK");
            return;
        }

        string path = EditorUtility.SaveFilePanel(
            "Export Audio Audit CSV",
            Application.dataPath,
            $"audio_audit_{DateTime.Now:yyyyMMdd_HHmmss}.csv",
            "csv"
        );

        if (string.IsNullOrEmpty(path))
            return;

        using (var sw = new StreamWriter(path))
        {
            sw.WriteLine("Selected,Kind,AssetPath,HierarchyPath,GameObject,ClipName,ClipPath,HasSoundCategory,SoundBus,HasMixerGroup,MixerGroupName,MixerAssetPath,Is3D,SpatialBlend");
            foreach (var r in _rows)
            {
                sw.WriteLine(string.Join(",",
                    r.selected ? "Y" : "N",
                    Csv(r.kind),
                    Csv(r.assetPath),
                    Csv(r.hierarchyPath),
                    Csv(r.goName),
                    Csv(r.clipName),
                    Csv(r.clipPath),
                    r.hasSoundCategory ? "Y" : "N",
                    Csv(r.hasSoundCategory ? r.soundBus : ""),
                    r.hasMixerGroup ? "Y" : "N",
                    Csv(r.mixerGroupName),
                    Csv(r.mixerAssetPath),
                    r.is3D ? "Y" : "N",
                    r.spatialBlend.ToString("0.###")
                ));
            }
        }

        EditorUtility.RevealInFinder(path);
        Debug.Log($"[AudioAudit&Fix] CSV Exported: {path}");
    }

    // -------------------- UTILS --------------------

    private static string BuildHierarchyPath(GameObject go)
    {
        var t = go.transform;
        string path = t.name;
        while (t.parent != null)
        {
            t = t.parent;
            path = t.name + "/" + path;
        }
        return path;
    }

    private static void PingAsset(string assetPath)
    {
        var obj = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetPath);
        if (obj)
        {
            EditorGUIUtility.PingObject(obj);
            Selection.activeObject = obj;
        }
    }

    private static bool Contains(string a, string b)
        => !string.IsNullOrEmpty(a) && a.IndexOf(b, StringComparison.OrdinalIgnoreCase) >= 0;

    private static string Truncate(string s, int max)
    {
        if (string.IsNullOrEmpty(s)) return "";
        if (s.Length <= max) return s;
        return s.Substring(0, max - 3) + "...";
    }

    private static string Csv(string s)
    {
        if (s == null) return "\"\"";
        s = s.Replace("\"", "\"\"");
        return $"\"{s}\"";
    }
}
#endif
