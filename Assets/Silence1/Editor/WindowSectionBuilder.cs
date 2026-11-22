using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Silence1.Editor
{
    public static class WindowSectionBuilder
    {
        const string ScenePath = "Assets/Scenes/Silence1Demo.unity";
        const string Stock = "Assets/Silence1/Section";
        const string SectionLayer = "Window Section";
        const float Top = .062f;
        static readonly Color32 Shell = new(77,83,81,255), Cut = new(202,202,187,255),
            Rubber = new(58,59,54,255), Glass = new(153,192,181,255), GlassEdge = new(213,228,219,255),
            Spacer = new(154,152,132,255), Cavity = new(47,53,52,255),
            SealRed = new(221,79,44,255), Rail = new(188,184,168,255);

        // An extruded footprint with separate cap and side colours. Each stock is one mesh.
        sealed class StockMesh
        {
            readonly List<Vector3> positions = new();
            readonly List<Color32> colours = new();
            readonly List<int> indices = new();

            void Face(Color32 tint, params Vector3[] corners)
            {
                int start = positions.Count;
                foreach (var p in corners) { positions.Add(p); colours.Add(tint); }
                indices.AddRange(new[]{start,start+1,start+2,start,start+2,start+3});
            }

            public void Strip(Vector2 a, Vector2 b, float width, float bottom, float top, Color32 side, Color32 cap)
            {
                var tangent = (b-a).normalized;
                var normal = new Vector2(-tangent.y,tangent.x) * (.5f * width);
                var footprint = new[]{a+normal,b+normal,b-normal,a-normal};
                var hi = new Vector3[4];
                var lo = new Vector3[4];
                for (int i=0; i<4; i++)
                {
                    hi[i] = new Vector3(footprint[i].x,top,footprint[i].y);
                    lo[i] = new Vector3(footprint[i].x,bottom,footprint[i].y);
                }
                Face(cap,hi);
                for (int i=0; i<4; i++)
                {
                    int j=(i+1)%4;
                    Face(side,hi[j],hi[i],lo[i],lo[j]);
                }
            }

            public void Block(float left,float right,float back,float front,float bottom,float top,Color32 side,Color32 cap)
            {
                float z=(back+front)/2;
                Strip(new(left,z),new(right,z),front-back,bottom,top,side,cap);
            }

            public void Save(string name, Transform parent, Material material)
            {
                string file = Stock + "/" + name + ".asset";
                var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(file);
                if (!mesh) { mesh = new Mesh(); AssetDatabase.CreateAsset(mesh,file); }
                mesh.Clear();
                mesh.name = name;
                mesh.SetVertices(positions);
                mesh.SetColors(colours);
                mesh.SetTriangles(indices,0);
                mesh.RecalculateNormals();
                mesh.RecalculateBounds();
                EditorUtility.SetDirty(mesh);
                Draw(parent,mesh,material);
            }
        }

        static void Draw(Transform part, Mesh mesh, Material material)
        {
            if (!part.TryGetComponent(out MeshFilter filter)) filter = part.gameObject.AddComponent<MeshFilter>();
            if (!part.TryGetComponent(out MeshRenderer renderer)) renderer = part.gameObject.AddComponent<MeshRenderer>();
            filter.sharedMesh = mesh;
            renderer.sharedMaterial = material;
        }

        static Transform Part(string name, Transform parent, int layer, Vector3 position)
        {
            var t = parent.Find(name);
            if (!t) { t = new GameObject(name).transform; t.SetParent(parent,false); }
            t.localPosition = position;
            t.gameObject.layer = layer;
            return t;
        }

        // Glazed ends are handed, leaving the two original sash transforms to set the spacing.
        static StockMesh Profile()
        {
            var m = new StockMesh();
            const float low=.004f;
            m.Block(-.030f,.033f,-.018f,.019f,low,low+.001f,Cavity,Cavity);
            m.Block(-.030f,.035f,-.019f,-.016f,low,Top,Shell,Cut);
            m.Block(-.030f,.034f,.016f,.019f,low,Top,Shell,Cut);
            foreach(float x in new[]{-.029f,-.010f,.012f,.032f})
                m.Block(x,x+.0023f,-.016f,.016f,low,Top,Shell,Cut);
            m.Strip(new(-.009f,-.002f),new(.011f,.006f),.002f,low,Top,Shell,Cut);
            foreach(float z in new[]{-.010f,.010f})
            {
                // The panes are rendered in a separate transparent pass.
                m.Block(.031f,.043f,z-.0038f,z+.0038f,low,Top-.0008f,Rubber,Rubber);
            }
            m.Block(.030f,.039f,-.008f,.008f,low,Top-.002f,Spacer,Spacer);
            for(int i=0;i<4;i++)
                m.Block(.033f,.036f,-.006f+i*.0033f,-.0048f+i*.0033f,low,Top-.0019f,Cavity,Cavity);
            return m;
        }

        static void AddGlass(Transform moving, Transform fixedPart)
        {
            var glass = AssetDatabase.LoadAssetAtPath<Material>(Stock+"/Glass edges.mat");
            if (!glass) { glass = new Material(Shader.Find("Silence1/Section glass")); AssetDatabase.CreateAsset(glass,Stock+"/Glass edges.mat"); }
            var panes = new StockMesh();
            foreach(float z in new[]{-.010f,.010f})
                panes.Block(.0388f,.998f,z-.0019f,z+.0019f,.004f,Top+.0005f,Glass,GlassEdge);
            var a = Part("Glass cut",moving,moving.gameObject.layer,Vector3.zero);
            panes.Save("Twin glass panes",a,glass);
            var b = Part("Glass cut",fixedPart,fixedPart.gameObject.layer,Vector3.zero);
            Draw(b,a.GetComponent<MeshFilter>().sharedMesh,glass);
        }

        static int Layer()
        {
            int layer = LayerMask.NameToLayer(SectionLayer);
            if (layer >= 0) return layer;
            var manager = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
            var layers = manager.FindProperty("layers");
            for(int i=8;i<layers.arraySize;i++)
            {
                var item = layers.GetArrayElementAtIndex(i);
                if (item.stringValue.Length != 0) continue;
                item.stringValue = SectionLayer;
                manager.ApplyModifiedProperties();
                return i;
            }
            throw new System.InvalidOperationException("A free layer is required for the window section camera.");
        }

        public static void Attach(SlidingWindow control)
        {
            int layer = Layer();
            if (!AssetDatabase.IsValidFolder(Stock)) AssetDatabase.CreateFolder("Assets/Silence1","Section");
            var mat = AssetDatabase.LoadAssetAtPath<Material>(Stock+"/Cut stock.mat");
            if (!mat) { mat = new Material(Shader.Find("Silence1/Section stock")); AssetDatabase.CreateAsset(mat,Stock+"/Cut stock.mat"); }
            var front = control.frontSash;
            var rear = front.parent.Find("Rear fixed sash");
            var moving = Part("Meeting section - moving",front,layer,new(.030f,0,0));
            var fixedPart = Part("Meeting section - fixed",rear,layer,new(.991f,0,0));
            fixedPart.localRotation = Quaternion.Euler(0,180,0);
            var profile = Profile();
            profile.Save("Hollow extrusion and glazing",moving,mat);
            Draw(fixedPart,moving.GetComponent<MeshFilter>().sharedMesh,mat);
            AddGlass(moving,fixedPart);

            var seal = Part("Flexible meeting seal",moving,layer,new(-.021f,0,0));
            var lip = new StockMesh();
            lip.Block(-.003f,.005f,.015f,.022f,.006f,Top+.001f,Rubber,SealRed);
            lip.Strip(new(.001f,.020f),new(.011f,.0268f),.0045f,.006f,Top+.001f,Rubber,SealRed);
            lip.Strip(new(.010f,.0265f),new(.0305f,.0265f),.004f,.006f,Top+.001f,Rubber,SealRed);
            lip.Save("Moving seal",seal,mat);
            var land = new StockMesh();
            land.Block(-.011f,-.006f,.016f,.0276f,.006f,Top+.0003f,Shell,Cut);
            land.Block(-.006f,-.00033f,.023f,.0276f,.006f,Top+.0003f,Rubber,Rubber);
            land.Save("Fixed seal seat",Part("Fixed seal seat",fixedPart,layer,Vector3.zero),mat);

            var basePoint = new Vector3(control.closedX+.030f,front.localPosition.y,front.localPosition.z);
            var deck = Part("Lower track section",front.parent,layer,basePoint);
            var sill = new StockMesh();
            sill.Block(-1.06f,1.06f,-.045f,.097f,-.012f,-.006f,Shell,Rail);
            foreach(float z in new[]{-.040f,-.005f,.053f,.088f})
                sill.Block(-1.06f,1.06f,z,z+.0025f,-.006f,.001f,Shell,Cut);
            sill.Save("Track bed",deck,mat);
            if (!control.TryGetComponent(out WindowSection section)) section = control.gameObject.AddComponent<WindowSection>();
            section.focus = Part("Joint camera target",deck,layer,new(-.082f,.042f,.025f));
            section.seal = seal;
            var surface = Part("Section drag surface",front,layer,Vector3.zero);
            if (!surface.TryGetComponent(out BoxCollider grip)) grip = surface.gameObject.AddComponent<BoxCollider>();
            grip.center = new(.5145f,.036f,0);
            grip.size = new(1.029f,.064f,.057f);
            section.grip = grip;
            control.section = section;
            control.roomCamera.cullingMask &= ~(1<<layer);
            EditorUtility.SetDirty(control);
            EditorUtility.SetDirty(control.roomCamera);
        }

        [MenuItem("SILENCE1/Install window section")]
        public static void Install()
        {
            var scene = SceneManager.GetSceneByPath(ScenePath);
            if (!scene.isLoaded) scene=EditorSceneManager.OpenScene(ScenePath,OpenSceneMode.Additive);
            foreach(var root in scene.GetRootGameObjects())
            {
                var control = root.GetComponent<SlidingWindow>();
                if (!control) continue;
                Attach(control);
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                AssetDatabase.SaveAssets();
                return;
            }
            throw new System.InvalidOperationException("Window controller missing from demo scene.");
        }
    }
}
