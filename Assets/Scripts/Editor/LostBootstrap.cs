using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Lost.EditorTools
{
    /// <summary>
    /// 一键把 D1 需要的工程规范、占位美术、灰盒场景、预制体全部生成出来。
    /// 菜单：Lost/1 设置工程、Lost/2 生成占位美术、Lost/3 生成灰盒场景、Lost/一键全部
    /// 也可以命令行跑：Unity.exe -batchmode -quit -projectPath ... -executeMethod Lost.EditorTools.LostBootstrap.SetupAll
    /// </summary>
    public static class LostBootstrap
    {
        const string ArtDir = "Assets/Art/Placeholder";
        const string PrefabPlayerDir = "Assets/Prefabs/Player";
        const string PrefabRingDir = "Assets/Prefabs/Ring";
        const string ScenePath = "Assets/Scenes/Greybox.unity";

        static readonly string[] LayerNames =
        {
            "Player", "PlayerRing", "Enemy", "EnemyAttack", "Ground",
            "OneWay", "Interact", "Hazard", "Pet"
        };

        static readonly string[] SortingLayerNames =
        {
            "Sky", "Mountain", "Decor", "Tiles", "Props", "Enemy", "Player", "Ring", "FX", "Darkness"
        };

        [MenuItem("Lost/一键全部（设置+美术+场景）", false, 1)]
        public static void SetupAll()
        {
            SetupProject();
            GeneratePlaceholderArt();
            BuildScene();
            AssetDatabase.SaveAssets();
            Debug.Log("[LostBootstrap] 全部完成：工程规范 / 占位美术 / Greybox 场景 / 预制体");
        }

        // ---------------- 1. 工程规范 ----------------
        [MenuItem("Lost/1 设置工程（图层·排序层·物理矩阵）", false, 20)]
        public static void SetupProject()
        {
            SetupLayers();
            SetupSortingLayers();
            SetupCollisionMatrix();
            SetupTimeAndScreen();
            AssetDatabase.SaveAssets();
            Debug.Log("[LostBootstrap] 工程规范已设置");
        }

        static SerializedObject TagManager()
        {
            var assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset");
            return new SerializedObject(assets[0]);
        }

        static void SetupLayers()
        {
            var so = TagManager();
            var layers = so.FindProperty("layers");
            foreach (var name in LayerNames)
            {
                if (LayerMask.NameToLayer(name) != -1) continue;   // 已存在就跳过
                for (int i = 8; i < layers.arraySize; i++)
                {
                    var el = layers.GetArrayElementAtIndex(i);
                    if (string.IsNullOrEmpty(el.stringValue))
                    {
                        el.stringValue = name;
                        break;
                    }
                }
            }
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static void SetupSortingLayers()
        {
            var so = TagManager();
            var arr = so.FindProperty("m_SortingLayers");
            var existing = new Dictionary<string, bool>();
            for (int i = 0; i < arr.arraySize; i++)
                existing[arr.GetArrayElementAtIndex(i).FindPropertyRelative("name").stringValue] = true;

            int id = 1000;
            foreach (var name in SortingLayerNames)
            {
                if (existing.ContainsKey(name)) continue;
                arr.arraySize++;
                var el = arr.GetArrayElementAtIndex(arr.arraySize - 1);
                el.FindPropertyRelative("name").stringValue = name;
                el.FindPropertyRelative("uniqueID").intValue = id++;
                existing[name] = true;
            }
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static void SetupCollisionMatrix()
        {
            // 关掉这些组合，避免玩家被敌人顶飞、环被自己挡住
            string[][] off =
            {
                new[] { "Player", "Enemy" },
                new[] { "Player", "PlayerRing" },
                new[] { "Enemy", "Enemy" },
                new[] { "PlayerRing", "PlayerRing" },
            };

            var so = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/Physics2DSettings.asset")[0]);
            var m = so.FindProperty("m_LayerCollisionMatrix");

            foreach (var pair in off)
            {
                int a = LayerMask.NameToLayer(pair[0]);
                int b = LayerMask.NameToLayer(pair[1]);
                if (a < 0 || b < 0 || a >= m.arraySize || b >= m.arraySize) continue;

                int va = m.GetArrayElementAtIndex(a).intValue;
                int vb = m.GetArrayElementAtIndex(b).intValue;
                m.GetArrayElementAtIndex(a).intValue = va & ~(1 << b);
                m.GetArrayElementAtIndex(b).intValue = vb & ~(1 << a);
            }
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static void SetupTimeAndScreen()
        {
            var tm = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TimeManager.asset")[0]);
            var fixedStep = tm.FindProperty("Fixed Timestep");
            if (fixedStep != null) fixedStep.floatValue = 0.0166f;
            tm.ApplyModifiedPropertiesWithoutUndo();

            var ps = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset")[0]);
            var w = ps.FindProperty("defaultScreenWidth");
            var h = ps.FindProperty("defaultScreenHeight");
            var resizable = ps.FindProperty("resizableWindow");
            if (w != null) w.intValue = 1920;
            if (h != null) h.intValue = 1080;
            if (resizable != null) resizable.boolValue = true;
            ps.ApplyModifiedPropertiesWithoutUndo();
        }

        // ---------------- 2. 占位美术 ----------------
        [MenuItem("Lost/2 生成占位美术", false, 30)]
        public static void GeneratePlaceholderArt()
        {
            if (!AssetDatabase.IsValidFolder("Assets/Art")) AssetDatabase.CreateFolder("Assets", "Art");
            if (!AssetDatabase.IsValidFolder(ArtDir)) AssetDatabase.CreateFolder("Assets/Art", "Placeholder");

            MakeSprite("Square", 64, 64, (x, y) => new Color(1, 1, 1, 1));
            MakeSprite("Circle", 64, 64, (x, y) =>
            {
                float dx = x - 31.5f, dy = y - 31.5f;
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                return new Color(1, 1, 1, Mathf.Clamp01(31.5f - d));
            });
            MakeSprite("RingShape", 96, 96, (x, y) =>
            {
                float dx = x - 47.5f, dy = y - 47.5f;
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                float band = Mathf.Clamp01(4f - Mathf.Abs(d - 34f));
                return new Color(1, 1, 1, band);
            });
            MakeSprite("Capsule", 44, 96, (x, y) =>
            {
                const float halfW = 21f, halfH = 47f, r = 21f;
                float cx = Mathf.Clamp(x - 21.5f, -(halfW - r), halfW - r);
                float dy = Mathf.Abs(y - 47.5f) - (halfH - r);
                float dx = (x - 21.5f) - cx;
                float d = Mathf.Sqrt(dx * dx + Mathf.Max(0f, dy) * Mathf.Max(0f, dy));
                return new Color(1, 1, 1, Mathf.Clamp01(r - d));
            });

            // 零摩擦物理材质：不做的话贴墙会粘住
            string matPath = ArtDir + "/Mat_Frictionless.physicsMaterial2D";
            if (!File.Exists(matPath))
            {
                var mat = new PhysicsMaterial2D("Mat_Frictionless");
                mat.friction = 0f;
                mat.bounciness = 0f;
                AssetDatabase.CreateAsset(mat, matPath);
            }
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[LostBootstrap] 占位美术已生成");
        }

        static void MakeSprite(string name, int w, int h, System.Func<int, int, Color> pixel)
        {
            string path = ArtDir + "/" + name + ".png";
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            var pixels = new Color[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    pixels[y * w + x] = pixel(x, y);
            tex.SetPixels(pixels);
            tex.Apply();
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);

            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = 64f;
            importer.alphaIsTransparency = true;
            importer.filterMode = FilterMode.Bilinear;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.mipmapEnabled = false;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
        }

        static Sprite LoadSprite(string name)
        {
            return AssetDatabase.LoadAssetAtPath<Sprite>(ArtDir + "/" + name + ".png");
        }

        // ---------------- 3. 灰盒场景 ----------------
        [MenuItem("Lost/3 生成灰盒场景（含预制体）", false, 40)]
        public static void BuildScene()
        {
            if (!AssetDatabase.IsValidFolder("Assets/Prefabs")) AssetDatabase.CreateFolder("Assets", "Prefabs");
            if (!AssetDatabase.IsValidFolder(PrefabPlayerDir)) AssetDatabase.CreateFolder("Assets/Prefabs", "Player");
            if (!AssetDatabase.IsValidFolder(PrefabRingDir)) AssetDatabase.CreateFolder("Assets/Prefabs", "Ring");

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // ---- 相机 ----
            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            camGo.transform.position = new Vector3(0f, 5f, -10f);
            var cam = camGo.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = 5.6f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Hex("#2B3A4F");
            cam.transform.position = new Vector3(0f, 5f, -10f);
            var follow = camGo.AddComponent<CameraFollow>();
            var feedback = camGo.AddComponent<HitFeedback>();
            feedback.CameraFollow = follow;

            // ---- 地形 ----
            var level = new GameObject("Level");
            Block(level.transform, "Ground_Left", 4f, -0.5f, 20f, 1f, "#E8EEF5");
            Block(level.transform, "Ground_Right", 24f, -0.5f, 12f, 1f, "#E8EEF5");
            Block(level.transform, "Pit_Floor", 16f, -3f, 4f, 1f, "#A8B8CC");
            Block(level.transform, "Platform_A", -1f, 2.6f, 3f, 0.5f, "#E8EEF5");
            Block(level.transform, "Platform_B", 5f, 4.2f, 4f, 0.5f, "#E8EEF5");
            Block(level.transform, "Platform_C", 16f, 2.8f, 4f, 0.5f, "#E8EEF5");
            Block(level.transform, "Wall_Right", 29.5f, 2.5f, 0.6f, 6f, "#A8B8CC");

            // ---- 出生点 ----
            var spawn = new GameObject("SpawnPoint");
            spawn.transform.position = new Vector3(-5f, 1f, 0f);

            // ---- 玩家 ----
            var player = new GameObject("Player");
            player.layer = LayerMask.NameToLayer("Player");
            player.tag = "Player";
            player.transform.position = new Vector3(-5f, 1.05f, 0f);

            var pr = player.AddComponent<SpriteRenderer>();
            pr.sprite = LoadSprite("Capsule");
            pr.color = Hex("#E8EEF5");
            pr.sortingLayerName = "Player";

            var rb = player.AddComponent<Rigidbody2D>();
            rb.bodyType = RigidbodyType2D.Dynamic;
            rb.gravityScale = 1.85f;
            rb.freezeRotation = true;
            rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            rb.interpolation = RigidbodyInterpolation2D.Interpolate;

            var pc = player.AddComponent<BoxCollider2D>();
            pc.size = new Vector2(0.7f, 1.5f);
            pc.offset = new Vector2(0f, -0.05f);
            pc.sharedMaterial = AssetDatabase.LoadAssetAtPath<PhysicsMaterial2D>(ArtDir + "/Mat_Frictionless.physicsMaterial2D");

            var input = player.AddComponent<PlayerInput>();
            var motor = player.AddComponent<PlayerMotor>();
            var ringSys = player.AddComponent<RingSystem>();
            var ctrl = player.AddComponent<PlayerController>();

            // 手
            var pivot = new GameObject("RingPivot");
            pivot.transform.SetParent(player.transform, false);
            pivot.transform.localPosition = new Vector3(0.45f, 0.2f, 0f);

            // 环
            var ringGo = new GameObject("Ring");
            ringGo.layer = LayerMask.NameToLayer("PlayerRing");
            ringGo.transform.SetParent(pivot.transform, false);
            var rrn = ringGo.AddComponent<SpriteRenderer>();
            rrn.sprite = LoadSprite("RingShape");
            rrn.color = Hex("#FFD98A");
            rrn.sortingLayerName = "Ring";
            rrn.sortingOrder = 10;
            ringGo.transform.localScale = new Vector3(0.55f, 0.55f, 1f);
            var ring = ringGo.AddComponent<RingProjectile>();   // 自动带 Kinematic 刚体 + Trigger 碰撞盒

            // 引用连线
            ctrl.Input = input;
            ctrl.Motor = motor;
            ctrl.Ring = ringSys;
            ctrl.RingPivot = pivot.transform;
            ringSys.Input = input;
            ringSys.Player = ctrl;
            ringSys.Ring = ring;
            ringSys.Hand = pivot.transform;
            follow.Target = player.transform;

            EditorUtility.SetDirty(ctrl);
            EditorUtility.SetDirty(ringSys);

            // ---- 预制体 ----
            PrefabUtility.SaveAsPrefabAsset(player, PrefabPlayerDir + "/Player.prefab");
            PrefabUtility.SaveAsPrefabAsset(ringGo, PrefabRingDir + "/Ring.prefab");

            // ---- 保存场景并进 Build Settings ----
            if (!AssetDatabase.IsValidFolder("Assets/Scenes")) AssetDatabase.CreateFolder("Assets", "Scenes");
            EditorSceneManager.SaveScene(scene, ScenePath);

            var list = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            if (!list.Exists(s => s.path == ScenePath))
                list.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = list.ToArray();

            Debug.Log("[LostBootstrap] 场景已生成：" + ScenePath);
        }

        static void Block(Transform parent, string name, float x, float y, float w, float h, string color)
        {
            var go = new GameObject(name);
            go.layer = LayerMask.NameToLayer("Ground");
            go.transform.SetParent(parent, false);
            go.transform.position = new Vector3(x, y, 0f);
            go.transform.localScale = new Vector3(w, h, 1f);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = LoadSprite("Square");
            sr.color = Hex(color);
            sr.sortingLayerName = "Tiles";
            go.AddComponent<BoxCollider2D>();
        }

        static Color Hex(string hex)
        {
            Color c;
            ColorUtility.TryParseHtmlString(hex, out c);
            return c;
        }
    }
}

