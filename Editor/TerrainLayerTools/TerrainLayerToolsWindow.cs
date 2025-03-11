#if UNITY_EDITOR

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace TerrainLayerTools {
    public class TerrainLayerToolsWindow : EditorWindow {
        Vector2 _scrollPosition;

        GameObject _terrainRoot;

        int _desiredNumberOfLayers = 4;
        bool _shouldPrintEmptyLayers = true;

        int _layerToRemove;

        int _layerToMerge;
        int _layerToMergeInto;

        [MenuItem("Tools/Terrain Layer Tools")]
        public static void ShowWindow() {
            var window = GetWindow<TerrainLayerToolsWindow>();
            window.titleContent = new GUIContent("Terrain Layer Tools");
            window.minSize = new Vector2(350, 150);
        }

        void OnGUI() {
            using var scrollView = new EditorGUILayout.ScrollViewScope(_scrollPosition);
            _scrollPosition = scrollView.scrollPosition;

            DrawTerrainRoot();

            if (_terrainRoot == null)
                return;

            DrawLayerUsageReporter();
            DrawLayerRemove();
            DrawLayerMerge();
            DrawNormalizeLayers();
            DrawRemoveEmptyLayers();
        }

        void DrawTerrainRoot() {
            GUILayout.Space(15);

            _terrainRoot = (GameObject)EditorGUILayout.ObjectField(
                "Terrain or terrain root", _terrainRoot, typeof(GameObject), allowSceneObjects: true);
        }

        void DrawLayerUsageReporter() {
            GUILayout.Space(15);
            GUILayout.Label("1. PRINT LAYER USAGE");

            EditorGUILayout.HelpBox("Checks the usage of terrain layers per terrain tile and " +
                                    "outputs the results to Console. Doesn't alter the terrain.",
                MessageType.Info);

            GUILayout.Space(10);

            _desiredNumberOfLayers = EditorGUILayout.IntField(
                new GUIContent("Desired number of layers",
                    "If a terrain tile has more layers then desired, the result is printed as error."),
                _desiredNumberOfLayers);

            _shouldPrintEmptyLayers = EditorGUILayout.Toggle(new GUIContent("Print empty layers",
                "When checked, empty layers are reported but not counted."), _shouldPrintEmptyLayers);

            GUILayout.Space(10);

            if (GUILayout.Button("Clear console"))
                ClearConsole();

            if (GUILayout.Button("Log layer usage")) {
                ProcessTerrains(_terrainRoot, (terrainName, terrainData) =>
                    LogTerrainLayerUsage(_shouldPrintEmptyLayers, _desiredNumberOfLayers, terrainName, terrainData));
            }
        }

        static void ClearConsole() {
            var editorAssembly = Assembly.GetAssembly(typeof(Editor));
            var logEntriesType = editorAssembly.GetType("UnityEditor.LogEntries");
            var clearMethod = logEntriesType.GetMethod("Clear");
            clearMethod?.Invoke(new object(), null);
        }

        static void LogTerrainLayerUsage(
            bool shouldPrintEmptyLayers, int desiredNumberOfLayers, string terrainName, TerrainData terrainData) {
            if (desiredNumberOfLayers < 0) {
                desiredNumberOfLayers = 0;
            }

            var terrainLayers = terrainData.terrainLayers;
            if (shouldPrintEmptyLayers) {
                var layersWithData = new List<int>();
                var emptyLayers = new List<int>();

                var alphamapWidth = terrainData.alphamapWidth;
                var alphamapHeight = terrainData.alphamapHeight;
                var alphamaps = terrainData.GetAlphamaps(0, 0, alphamapWidth, alphamapHeight);
                for (var i = 0; i < terrainLayers.Length; i++) {
                    var doesLayerHaveData = DoLayerPixelsHaveData(alphamapWidth, alphamapHeight, alphamaps, i);
                    if (doesLayerHaveData)
                        layersWithData.Add(i);
                    else
                        emptyLayers.Add(i);
                }

                var stringBuilder = new StringBuilder();
                stringBuilder.Append($"Terrain '{terrainName}' has {layersWithData.Count} layers with data (");
                foreach (var layer in layersWithData) {
                    stringBuilder.Append(layer);
                    stringBuilder.Append(",");
                }

                stringBuilder.Remove(stringBuilder.Length - 1, 1);
                stringBuilder.Append($") and {emptyLayers.Count} empty layers (");
                foreach (var layer in emptyLayers) {
                    stringBuilder.Append(layer);
                    stringBuilder.Append(",");
                }

                stringBuilder.Remove(stringBuilder.Length - 1, 1);
                stringBuilder.Append(")");

                if (layersWithData.Count <= desiredNumberOfLayers) {
                    Debug.Log(stringBuilder);
                }
                else {
                    Debug.LogError(stringBuilder);
                }
            }
            else {
                var message = $"Terrain '{terrainName}' has {terrainLayers.Length} layers";
                if (terrainLayers.Length <= desiredNumberOfLayers) {
                    Debug.Log(message);
                }
                else {
                    Debug.LogError(message);
                }
            }
        }

        void DrawLayerRemove() {
            GUILayout.Space(15);
            GUILayout.Label("2. REMOVE LAYER");

            EditorGUILayout.HelpBox("Removes a terrain layer and rearranges the remaining " +
                                    "layers reducing layer count by 1.", MessageType.Info);

            EditorGUILayout.HelpBox(
                "WARNING! THIS OPERATION CANNOT BE UNDONE! BACKUP YOUR PROJECT BEFORE PROCEEDING!",
                MessageType.Warning);

            GUILayout.Space(10);

            _layerToRemove = EditorGUILayout.IntField(
                new GUIContent("Remove layer", "Index of the first layer is 0"), _layerToRemove);

            GUILayout.Space(10);

            if (GUILayout.Button("Remove")) {
                ProcessTerrains(_terrainRoot, (terrainName, terrainData) =>
                    RemoveTerrainLayer(_layerToRemove, terrainName, terrainData), SaveAssets);
            }
        }

        static void RemoveTerrainLayer(int layerIndex, string terrainName, TerrainData terrainData) {
            var alphamapLayers = terrainData.alphamapLayers;
            var terrainLayers = terrainData.terrainLayers;
            if (layerIndex < 0 || layerIndex >= terrainLayers.Length || layerIndex >= alphamapLayers) {
                Debug.LogWarning($"Layer index {layerIndex} is out of bounds for terrain {terrainName} with " +
                                 $"alphamap layers count {alphamapLayers} and terrain layers count " +
                                 $"{terrainLayers.Length}. Skip processing of this terrain.");

                return;
            }

            var alphamapWidth = terrainData.alphamapWidth;
            var alphamapHeight = terrainData.alphamapHeight;
            var alphamaps = terrainData.GetAlphamaps(0, 0, alphamapWidth, alphamapHeight);

            var newTerrainLayers =
                RemoveTerrainLayer(layerIndex, alphamapWidth, alphamapHeight, alphamaps, terrainLayers);

            terrainData.SetAlphamaps(0, 0, alphamaps);
            terrainData.terrainLayers = newTerrainLayers;
        }

        static TerrainLayer[] RemoveTerrainLayer(int layerIndex, int alphamapWidth, int alphamapHeight,
            float[,,] alphamaps, TerrainLayer[] terrainLayers) {
            for (var i = layerIndex + 1; i < terrainLayers.Length; i++)
                MovePixels(alphamapWidth, alphamapHeight, alphamaps, i, i - 1);

            var newTerrainLayers = new TerrainLayer[terrainLayers.Length - 1];
            for (var i = 0; i < terrainLayers.Length; i++) {
                if (i < layerIndex)
                    newTerrainLayers[i] = terrainLayers[i];
                else if (i > layerIndex)
                    newTerrainLayers[i - 1] = terrainLayers[i];
            }

            return newTerrainLayers;
        }

        static void MovePixels(
            int alphamapWidth, int alphamapHeight, float[,,] alphamaps, int sourceLayer, int targetLayer) {
            for (var x = 0; x < alphamapWidth; x++)
            for (var y = 0; y < alphamapHeight; y++) {
                alphamaps[x, y, targetLayer] = alphamaps[x, y, sourceLayer];
                alphamaps[x, y, sourceLayer] = 0;
            }
        }

        void DrawLayerMerge() {
            GUILayout.Space(15);
            GUILayout.Label("3. MERGE LAYERS");

            EditorGUILayout.HelpBox("Merges a terrain layer into another one. Rearranges remaining " +
                                    "layers reducing layer count by 1. Useful when you want to optimize " +
                                    "out multiple layers sharing the same texture.", MessageType.Info);

            EditorGUILayout.HelpBox(
                "WARNING! THIS OPERATION CANNOT BE UNDONE! BACKUP YOUR PROJECT BEFORE PROCEEDING!",
                MessageType.Warning);

            GUILayout.Space(10);

            _layerToMerge = EditorGUILayout.IntField(
                new GUIContent("Merge layer", "Index of the first layer is 0"), _layerToMerge);

            _layerToMergeInto = EditorGUILayout.IntField(
                new GUIContent("into layer", "Index of the first layer is 0"), _layerToMergeInto);

            GUILayout.Space(10);

            if (GUILayout.Button("Merge")) {
                ProcessTerrains(_terrainRoot, (terrainName, terrainData) =>
                    MergeTerrainLayers(_layerToMerge, _layerToMergeInto, terrainName, terrainData), SaveAssets);
            }
        }

        static void MergeTerrainLayers(int sourceLayer, int targetLayer, string terrainName, TerrainData terrainData) {
            if (sourceLayer == targetLayer)
                return;

            var alphamapLayers = terrainData.alphamapLayers;
            var terrainLayers = terrainData.terrainLayers;
            if (sourceLayer < 0 || sourceLayer >= terrainLayers.Length || sourceLayer >= alphamapLayers) {
                Debug.LogWarning($"Source layer index {sourceLayer} is out of bounds for terrain {terrainName} " +
                                 $"with alphamap layers count {alphamapLayers} and terrain layers count" +
                                 $" {terrainLayers.Length}. Skip processing of this terrain.");

                return;
            }

            if (targetLayer < 0 || targetLayer >= terrainLayers.Length || targetLayer >= alphamapLayers) {
                Debug.LogWarning($"Target layer index {targetLayer} is out of bounds for terrain {terrainName} " +
                                 $"with alphamap layers count {alphamapLayers} and terrain layers count" +
                                 $" {terrainLayers.Length}. Skip processing of this terrain.");

                return;
            }

            var alphamapWidth = terrainData.alphamapWidth;
            var alphamapHeight = terrainData.alphamapHeight;
            var alphamaps = terrainData.GetAlphamaps(0, 0, alphamapWidth, alphamapHeight);
            MergePixels(alphamapWidth, alphamapHeight, alphamaps, sourceLayer, targetLayer);

            var newTerrainLayers =
                RemoveTerrainLayer(sourceLayer, alphamapWidth, alphamapHeight, alphamaps, terrainLayers);

            terrainData.SetAlphamaps(0, 0, alphamaps);
            terrainData.terrainLayers = newTerrainLayers;
        }

        static void MergePixels(
            int alphamapWidth, int alphamapHeight, float[,,] alphamaps, int sourceLayer, int targetLayer) {
            for (var x = 0; x < alphamapWidth; x++)
            for (var y = 0; y < alphamapHeight; y++) {
                alphamaps[x, y, targetLayer] += alphamaps[x, y, sourceLayer];
                alphamaps[x, y, sourceLayer] = 0;
            }
        }

        void DrawNormalizeLayers() {
            GUILayout.Space(15);
            GUILayout.Label("4. NORMALIZE LAYERS");

            EditorGUILayout.HelpBox("Makes the sum of all layers always be around 1 in every point of terrain. " +
                                    "Also fixes the dead pixels of the splatmap by blending the neighbor pixels. " +
                                    "Useful for when you remove layers or scale the splatmaps.",
                                    MessageType.Info);

            EditorGUILayout.HelpBox(
                "WARNING! THIS OPERATION CANNOT BE UNDONE! BACKUP YOUR PROJECT BEFORE PROCEEDING!",
                MessageType.Warning);

            if (GUILayout.Button("Normalize layers"))
                ProcessTerrains(_terrainRoot, NormalizeTerrainLayers, SaveAssets);
        }

        static void NormalizeTerrainLayers(string terrainName, TerrainData terrainData) {
            var terrainLayers = terrainData.terrainLayers;
            var alphamapWidth = terrainData.alphamapWidth;
            var alphamapHeight = terrainData.alphamapHeight;
            var alphamaps = terrainData.GetAlphamaps(0, 0, alphamapWidth, alphamapHeight);

            for (var x = 0; x < alphamapWidth; x++)
            for (var y = 0; y < alphamapHeight; y++) {
                var sumOfLayersWithoutData = 0f;
                var sumOfLayersWithData = 0f;
                var layersWithData = new List<int>();
                for (var i = 0; i < terrainLayers.Length; i++) {
                    var value = alphamaps[x, y, i];
                    if (Mathf.Approximately(value, 0))
                        sumOfLayersWithoutData += value;
                    else {
                        layersWithData.Add(i);
                        sumOfLayersWithData += value;
                    }
                }

                if (Mathf.Approximately(sumOfLayersWithData, 0f)) {
                    for (var i = 0; i < terrainLayers.Length; i++)
                        FixEmptyPixelInLayer(x, y, i, alphamaps);

                    // Run normalization again after blending the neighbors.
                    x--;
                    y--;
                    continue;
                }

                var multiplier =  (1f - sumOfLayersWithoutData) / sumOfLayersWithData;
                foreach (var layer in layersWithData) {
                    var value = alphamaps[x, y, layer];
                    value *= multiplier;
                    alphamaps[x, y, layer] = value;
                }
            }

            terrainData.SetAlphamaps(0, 0, alphamaps);
        }

        static void FixEmptyPixelInLayer(int x, int y, int layer, float[,,] alphamaps) {
            var sum = 0f;
            var count = 0;
            for (var i = x - 1; i <= x + 1; i++)
            for (var j = y - 1; j <= y + 1; j++) {
                if (i == x && j == y)
                    continue;

                if (!TryGetAlphamapValue(i, j, layer, alphamaps, out var value))
                    continue;

                sum += value;
                count++;
            }

            alphamaps[x, y, layer] = sum / count;
        }

        static bool TryGetAlphamapValue(int x, int y, int layer, float[,,] alphamaps, out float value) {
            if (x < 0) {
                value = 0f;
                return false;
            }

            if (x >= alphamaps.GetLength(0)) {
                value = 0f;
                return false;
            }

            if (y < 0) {
                value = 0f;
                return false;
            }

            if (y >= alphamaps.GetLength(1)) {
                value = 0f;
                return false;
            }

            value = alphamaps[x, y, layer];
            return true;
        }

        void DrawRemoveEmptyLayers() {
            GUILayout.Space(15);
            GUILayout.Label("5. REMOVE EMPTY LAYERS");

            EditorGUILayout.HelpBox("Removes unused terrain layers and rearranges the remaining layers reducing " +
                                    "layer count by the number of empty layers. Especially useful for multi-terrain " +
                                    "landscapes with biomes where tiles don't utilize all the layers.",
                                    MessageType.Info);

            EditorGUILayout.HelpBox(
                "WARNING! THIS OPERATION CANNOT BE UNDONE! BACKUP YOUR PROJECT BEFORE PROCEEDING!",
                MessageType.Warning);

            EditorGUILayout.HelpBox(
                "IMPORTANT! TERRAIN LAYERS OF DIFFERENT TILES WILL LIKELY NOT MATCH AFTERWARDS! " +
                "THE TOOLS ABOVE OR TERRAIN PAINTING MAY NOT WORK AS EXPECTED!", MessageType.Error);

            if (GUILayout.Button("Remove empty layers"))
                ProcessTerrains(_terrainRoot, (_, terrainData) => RemoveEmptyTerrainLayers(terrainData), SaveAssets);
        }

        static void RemoveEmptyTerrainLayers(TerrainData terrainData) {
            var terrainLayers = terrainData.terrainLayers;
            var alphamapWidth = terrainData.alphamapWidth;
            var alphamapHeight = terrainData.alphamapHeight;
            var alphamaps = terrainData.GetAlphamaps(0, 0, alphamapWidth, alphamapHeight);

            var emptyLayers = new Queue<int>();
            var layersWithData = new List<bool>();
            for (var i = 0; i < terrainLayers.Length; i++) {
                var doesLayerHaveData = DoLayerPixelsHaveData(alphamapWidth, alphamapHeight, alphamaps, i);
                if (!doesLayerHaveData) {
                    layersWithData.Add(false);
                    emptyLayers.Enqueue(i);
                    continue;
                }

                if (emptyLayers.Count == 0) {
                    layersWithData.Add(true);
                    continue;
                }

                var targetLayer = emptyLayers.Dequeue();
                SwapPixels(alphamapWidth, alphamapHeight, alphamaps, i, targetLayer);
                (terrainLayers[i], terrainLayers[targetLayer]) = (terrainLayers[targetLayer], terrainLayers[i]);
                layersWithData[targetLayer] = true;
                layersWithData.Add(false);
                emptyLayers.Enqueue(i);
            }

            var newTerrainLayers = layersWithData
                .TakeWhile(hasData => hasData)
                .Select((_, i) => terrainLayers[i])
                .ToArray();

            terrainData.SetAlphamaps(0, 0, alphamaps);
            terrainData.terrainLayers = newTerrainLayers;
        }

        static bool DoLayerPixelsHaveData(
            int alphamapWidth, int alphamapHeight, float[,,] alphamaps, int layer) {
            for (var x = 0; x < alphamapWidth; x++)
            for (var y = 0; y < alphamapHeight; y++) {
                if (!Mathf.Approximately(alphamaps[x, y, layer], 0))
                    return true;
            }

            return false;
        }

        static void SwapPixels(
            int alphamapWidth, int alphamapHeight, float[,,] alphamaps, int sourceLayer, int targetLayer) {
            for (var x = 0; x < alphamapWidth; x++)
            for (var y = 0; y < alphamapHeight; y++) {
                (alphamaps[x, y, sourceLayer], alphamaps[x, y, targetLayer]) =
                    (alphamaps[x, y, targetLayer], alphamaps[x, y, sourceLayer]);
            }
        }

        static void ProcessTerrains(GameObject terrainRoot,
            Action<string, TerrainData> processTerrainLayers,
            Action saveResults = null) {
            try {
                var terrainRootTransform = terrainRoot.transform;
                var childCount = terrainRootTransform.childCount;

                var progressIndex = 0;
                var progressTarget = childCount;

                var rootTerrain = terrainRootTransform.GetComponent<Terrain>();
                if (rootTerrain != null) {
                    progressIndex++;
                    progressTarget++;

                    EditorUtility.DisplayProgressBar(
                        "Terrain Layers Tool", $"Processing terrain tile {0} of {progressTarget}", 0);

                    ProcessTerrain(rootTerrain, terrainRoot.name, processTerrainLayers);
                }

                for (var i = 0; i < childCount; i++, progressIndex++) {
                    EditorUtility.DisplayProgressBar(
                        "Terrain Layer Tools",
                        $"Processing terrain tile {progressIndex} of {progressTarget}",
                        i / (float)progressTarget);

                    var terrainTransform = terrainRootTransform.GetChild(i);
                    var terrain = terrainTransform.GetComponent<Terrain>();
                    if (terrain == null) {
                        Debug.LogWarning($"{terrainTransform.gameObject.name} game object " +
                                         "is not a terrain. Skip processing of this terrain.");

                        continue;
                    }

                    ProcessTerrain(terrain, terrainTransform.gameObject.name, processTerrainLayers);
                }

                saveResults?.Invoke();
            }
            finally {
                EditorUtility.ClearProgressBar();
            }
        }

        static void ProcessTerrain(Terrain terrain, string terrainName,
            Action<string, TerrainData> processTerrainLayers) {
            var terrainData = terrain.terrainData;
            if (terrainData == null) {
                Debug.LogWarning($"{terrainName} terrain has no terrain data. Skip processing of this terrain.");
                return;
            }

            var alphamapLayers = terrainData.alphamapLayers;
            var terrainLayers = terrainData.terrainLayers;
            if (alphamapLayers != terrainLayers.Length) {
                Debug.LogWarning($"Alphamap layers count {alphamapLayers} of terrain {terrainName} is not equal to " +
                                 $"terrain layers count {terrainLayers.Length}. Skip processing of this terrain.");

                return;
            }

            processTerrainLayers.Invoke(terrainName, terrainData);
        }

        static void SaveAssets() {
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }
    }
}

#endif