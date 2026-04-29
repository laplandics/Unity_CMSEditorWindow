using System;
using System.Collections.Generic;
using Config;
using Constant.Enums;
using R3;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using World;

public class CMSEditorWindow : EditorWindow
{
    [SerializeField] private VisualTreeAsset windowAsset;
    [SerializeField] private VisualTreeAsset cmsInspector;

    [SerializeField] private VisualTreeAsset entitySystemListElement;
    
    private readonly ReactiveProperty<SystemConfigBase> _currentSystem = new();
    private readonly ReactiveProperty<EntityConfig> _currentEntity = new();
    
    private IDisposable _systemChangedSubscription;
    
    [MenuItem("Window/CMS")]
    public static void ShowExample()
    {
        var wnd = GetWindow<CMSEditorWindow>();
        wnd.titleContent = new GUIContent("CMS");
    }

    public void CreateGUI()
    {
        _currentEntity.Value = null;
        _currentEntity.Value = null;
        
        var window = windowAsset.Instantiate().Q<VisualElement>("ROOT");

        var chosenEntityField = window.Q<ObjectField>("ChosenEntityField");
        chosenEntityField.RegisterValueChangedCallback(e => _currentEntity.Value = e.newValue as EntityConfig);

        var addEntityButton = window.Q<Button>("AddEntityButton");
        addEntityButton.clicked -= OnAddEntityButtonClicked;
        addEntityButton.clicked += OnAddEntityButtonClicked;
        
        rootVisualElement.Add(window);
        
        _currentEntity.Subscribe(OnCurrentEntityChanged);
    }

    private void OnAddEntityButtonClicked()
    {
        const string directory = "Assets/Resources/Config/Entity";
        var existingAssetsCount = AssetDatabase.FindAssets("", new []{directory}).Length;
        var suffix = existingAssetsCount == 0 ? "" : $" ({existingAssetsCount})";
        
        var path = $"{directory}/New Entity{suffix}.asset";
        var asset = CreateInstance<EntityConfig>();
        
        AssetDatabase.CreateAsset(asset, path);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        
        _currentEntity.Value = asset;
    }

    private void OnDeleteEntityButtonClicked()
    { 
        var path = AssetDatabase.GetAssetPath(_currentEntity.Value);
        if (!EditorUtility.DisplayDialog(
            "Delete Entity?",
            $"This action will delete {path} from the assets. Are you sure?",
            "Delete",
            "Cancel"))
            return;
        
        _currentSystem.Value = null;
        _currentEntity.Value = null;
        
        AssetDatabase.DeleteAsset(path);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
    }

    private void OnCurrentEntityChanged(EntityConfig entity)
    {
        SetAssetToObjectField(entity);
        ShowEntityInspector(entity);
    }

    private void SetAssetToObjectField(EntityConfig entity)
    {
        var chosenEntityField = rootVisualElement.Q<ObjectField>("ChosenEntityField");
        chosenEntityField.value = entity;
    }

    private void ShowEntityInspector(EntityConfig entity)
    {
        _currentSystem.Value = null;

        if (rootVisualElement.Q<VisualElement>("InspectorContent").childCount != 0)
        { rootVisualElement.Q<VisualElement>("InspectorContent").RemoveAt(0); }

        if (entity == null) return;
        
        var inspectorWindow = cmsInspector.Instantiate().Q<VisualElement>("ROOT");
        
        var assetNameField = inspectorWindow.Q<TextField>("AssetNameField");
        assetNameField.value = entity.name;
        assetNameField.RegisterValueChangedCallback(e =>
        {
            if (string.IsNullOrEmpty(e.newValue)) return;
            
            var path = AssetDatabase.GetAssetPath(entity);
            AssetDatabase.RenameAsset(path, e.newValue);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            
            entity.name = e.newValue;
        });
        
        var entityKeyField = inspectorWindow.Q<EnumField>("EntityKeyEnum");
        entityKeyField.value = entity.key;
        entityKeyField.RegisterValueChangedCallback(e =>
        {
            entity.key = (Entities)e.newValue;
            EditorUtility.SetDirty(entity);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        });
        
        var addSystemButton = inspectorWindow.Q<Button>("AddSystemButton");
        addSystemButton.clicked -= ShowSystemSearchWindow;
        addSystemButton.clicked += ShowSystemSearchWindow;
        
        var deleteEntityButton = inspectorWindow.Q<Button>("DeleteEntityButton");
        deleteEntityButton.clicked -= OnDeleteEntityButtonClicked;
        deleteEntityButton.clicked += OnDeleteEntityButtonClicked;
        
        var spawnEntityButton = inspectorWindow.Q<Button>("SpawnEntityButton");
        spawnEntityButton.clicked -= OnSpawnEntityButtonClicked;
        spawnEntityButton.clicked += OnSpawnEntityButtonClicked;
        
        rootVisualElement.Q<VisualElement>("InspectorContent").Add(inspectorWindow);
        RefreshEntitySystemsList();
        
        _systemChangedSubscription?.Dispose();
        _systemChangedSubscription = _currentSystem.Subscribe(OnCurrentSystemChanged);
    }

    private void OnSpawnEntityButtonClicked()
    {
        var entityObject = new GameObject(_currentEntity.Value.key.ToString());
        entityObject.transform.position = Vector3.zero;
        entityObject.transform.rotation = Quaternion.identity;
        var entity = entityObject.AddComponent<Entity>();
        
    }

    private void RefreshEntitySystemsList()
    {
        var entitySystemsList = rootVisualElement.Q<ListView>("EntitySystemsList");
        entitySystemsList.selectionChanged -= OnSystemSelectedFromEntitySystemsList;

        entitySystemsList.itemsSource = _currentEntity.Value.systems;
        
        entitySystemsList.makeItem = MakeItem;
        entitySystemsList.bindItem = BindItem;

        entitySystemsList.selectionChanged += OnSystemSelectedFromEntitySystemsList;
        return;

        VisualElement MakeItem()
        {
            var eSystemLe = entitySystemListElement.Instantiate().Q<VisualElement>("ROOT");
            return eSystemLe;
        }
        
        void BindItem(VisualElement eSystemLe, int index)
        {
            var system = _currentEntity.Value.systems[index];
            var label = eSystemLe.Q<Label>("SystemName");
            var deleteButton = eSystemLe.Q<Button>("DeleteSystemButton");
            
            label.text = system.key.ToString();

            deleteButton.clicked -= deleteButton.userData as Action;
            Action action = () =>
            {
                if (_currentSystem.Value == system) _currentSystem.Value = null;
                _currentEntity.Value.systems.Remove(system);
                
                EditorUtility.SetDirty(_currentEntity.Value);
                AssetDatabase.RemoveObjectFromAsset(system);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                
                entitySystemsList.ClearSelection();
                entitySystemsList.Rebuild();
            };
            
            deleteButton.userData = action;
            deleteButton.clicked += action;
        }
    }

    private void OnSystemSelectedFromEntitySystemsList(IEnumerable<object> obj)
    {
        object selected = null;
        foreach (var selection in obj) selected = selection;

        var system = selected as SystemConfigBase;
        if (_currentSystem.Value == system) return;
        _currentSystem.Value = system;
    }

    private void OnCurrentSystemChanged(SystemConfigBase system)
    {
        HideSystemSearchWindow();
        ShowSystemInspector();
    }

    private void HideSystemSearchWindow()
    {
        var systemSearchWindow = rootVisualElement.Q<VisualElement>("SystemSearchWindow");
        if (!systemSearchWindow.visible) return;
        
        var allSystemsList = rootVisualElement.Q<ListView>("AllSystemsList");
        allSystemsList.selectionChanged -= OnSystemSelectedFromAllSystemsList;
        allSystemsList.ClearSelection();
        allSystemsList.Clear();
        allSystemsList.itemsSource = null;
        
        systemSearchWindow.visible = false;
    }
    
    private void ShowSystemSearchWindow()
    {
        var systemSearchWindow = rootVisualElement.Q<VisualElement>("SystemSearchWindow");
        
        if (systemSearchWindow.visible) { systemSearchWindow.visible = false; return; }
        
        systemSearchWindow.visible = true;
        systemSearchWindow.BringToFront();
        
        var allSystems = Enum.GetNames(typeof(Systems));
        
        var allSystemsList = systemSearchWindow.Q<ListView>("AllSystemsList");
        allSystemsList.selectionChanged -= OnSystemSelectedFromAllSystemsList;
        allSystemsList.itemsSource = allSystems;
        allSystemsList.selectionChanged += OnSystemSelectedFromAllSystemsList;
    }

    private void OnSystemSelectedFromAllSystemsList(IEnumerable<object> _)
    {
        var allSystemsList = rootVisualElement.Q<ListView>("AllSystemsList");
        
        var selection = allSystemsList.selectedItem;
        if (selection == null) return;

        var selectedSystemName = selection.ToString();
        var selectionSystemType = Type.GetType($"{nameof(Config)}.{selectedSystemName}Config, Assembly-CSharp");
        
        var systemInstance = CreateInstance(selectionSystemType) as SystemConfigBase;
        
        AddSystemToEntity(systemInstance);
        
        allSystemsList.ClearSelection();
        HideSystemSearchWindow();
    }

    private void AddSystemToEntity(SystemConfigBase system)
    {
        var path = AssetDatabase.GetAssetPath(_currentEntity.Value);
        system.name = system.GetType().Name;
        
        AssetDatabase.AddObjectToAsset(system, path);
        _currentEntity.Value.systems.Add(system);

        RefreshEntitySystemsList();
        
        EditorUtility.SetDirty(_currentEntity.Value);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
    }

    private void ShowSystemInspector()
    {
        var systemInspectorContent = rootVisualElement.Q<VisualElement>("SystemInspectorContent");
        var currentInspector = systemInspectorContent.Q<InspectorElement>();
        if (currentInspector != null) systemInspectorContent.Remove(currentInspector);
        
        if (_currentSystem.Value == null) return;
        
        var so = new SerializedObject(_currentSystem.Value);
        var inspector = new InspectorElement(so);
        
        systemInspectorContent.Add(inspector);
    }
}
