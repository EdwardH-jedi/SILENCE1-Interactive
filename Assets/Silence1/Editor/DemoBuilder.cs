using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace Silence1.Editor
{
    public static class DemoBuilder
    {
        const string Folder = "Assets/Silence1/";
        const string SceneFile = "Assets/Scenes/Silence1Demo.unity";
        const float InnerWidth = 1.99f, Height = .81f, SashWidth = 1.029f;
        const float Left = -1.695f, Bottom = .955f;
        static Material ink, powder, edge, metal, glass, gasket, red;

        static Material Finish(string name, string hex, float relief = 0, string image = null, bool transparent = false)
        {
            string path = Folder + "Materials/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(Shader.Find("Silence1/Architectural Ink"));
                AssetDatabase.CreateAsset(material, path);
            }
            ColorUtility.TryParseHtmlString("#" + hex, out var color);
            material.color = color;
            material.SetFloat("_Relief", relief);
            if (image != null) material.mainTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(Folder + "Art/" + image);
            if (transparent)
            {
                material.SetFloat("_SrcBlend", (int)BlendMode.SrcAlpha);
                material.SetFloat("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
                material.SetFloat("_ZWrite", 0);
                material.renderQueue = 3000;
            }
            EditorUtility.SetDirty(material);
            return material;
        }

        static Transform Group(string name, Transform parent)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            return t;
        }

        static void SketchProportions(Transform layer)
        {
            // Match the two-leaf ink study around the centre of the opening.
            layer.localScale = new(.74f,1.32f,1);
            layer.localPosition = new(-.182f,-.4352f,0);
        }

        static GameObject Shape(string name, Transform parent, Vector3 xyz, Vector3 size, Material finish, PrimitiveType type = PrimitiveType.Cube)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = xyz;
            go.transform.localScale = size;
            go.GetComponent<MeshRenderer>().sharedMaterial = finish;
            UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>());
            return go;
        }

        static void Profile(Transform parent, string name, float x, float y, float z, float w, float h, float t, float d, Material finish)
        {
            var group = Group(name, parent);
            Shape("Left stile", group,new(x+t/2,y+h/2,z),new(t,h,d),finish);
            Shape("Right stile",group,new(x+w-t/2,y+h/2,z),new(t,h,d),finish);
            Shape("Head",group,new(x+w/2,y+h-t/2,z),new(w-2*t,t,d),finish);
            Shape("Sill",group,new(x+w/2,y+t/2,z),new(w-2*t,t,d),finish);
        }

        static Transform Sash(Transform window, string name, float x, float z, bool front)
        {
            var t = Group(name,window);
            t.localPosition = new(x,Bottom,z);
            Profile(t,"Charcoal extrusion",0,0,0,SashWidth,Height,.038f,.030f,ink);
            Profile(t,"Powder coated face",.004f,.004f,-.016f,SashWidth-.008f,Height-.008f,.027f,.005f,front ? powder : edge);
            Profile(t,"Glazing gasket",.033f,.033f,-.011f,SashWidth-.066f,Height-.066f,.006f,.010f,gasket);
            Profile(t,"Spacer",.039f,.039f,.002f,SashWidth-.078f,Height-.078f,.004f,.008f,metal);
            Shape("Insulating glass",t,new(SashWidth/2,Height/2,0),new(SashWidth-.077f,Height-.077f,1),glass,PrimitiveType.Quad);
            return t;
        }

        static AudioSource Source(GameObject go, string clip, bool loop)
        {
            var source = go.AddComponent<AudioSource>();
            source.clip = AssetDatabase.LoadAssetAtPath<AudioClip>(Folder+"Audio/"+clip+".wav");
            source.loop = loop;
            source.playOnAwake = false;
            source.spatialBlend = 0;
            return source;
        }

        [MenuItem("SILENCE1/Create demo scene")]
        public static void CreateScene()
        {
            if (File.Exists(SceneFile)) throw new InvalidOperationException("Demo scene already exists.");
            // Additive creation keeps other open scenes and unsaved work intact.
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            SceneManager.SetActiveScene(scene);
            ink = Finish("Charcoal","343333",1);
            powder = Finish("Warm enamel","DFDDD4",1);
            edge = Finish("Rear enamel","B9B9AE",1);
            metal = Finish("Spacer","ABA9A0",1);
            gasket = Finish("Glazing rubber","504D48",1);
            red = Finish("Latch index","DF4A2B");
            glass = Finish("Low iron glass","E4EAE567",transparent:true);
            var camera = new GameObject("Interior camera").AddComponent<Camera>();
            camera.tag = "MainCamera";
            camera.transform.SetPositionAndRotation(new(-.05f,1.4f,.05f),Quaternion.Euler(3,-12,0));
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color32(246,244,238,255);
            camera.fieldOfView = 2*Mathf.Atan(360f/1280f)*Mathf.Rad2Deg;
            camera.nearClipPlane = .1f;
            camera.farClipPlane = 15;
            camera.allowHDR = false;
            camera.gameObject.AddComponent<AudioListener>();
            var room = Group("Reading room",null);
            const float plateDistance = 8;
            var back = Shape("Interior illustration",room,camera.transform.position + camera.transform.forward*plateDistance,
                new(plateDistance,plateDistance*720/1280,1),Finish("Reading room","FFFFFF",image:"ReadingRoom.jpg"),PrimitiveType.Quad);
            back.transform.rotation = camera.transform.rotation;

            var outside = Group("Exterior",null);
            SketchProportions(outside);
            Shape("Rooftop backdrop",outside,new(-.7f,1.36f,3.18f),new(2.11f,.925f,1),
                Finish("Rooftop view","FFFFFF",image:"Rooftops.jpg"),PrimitiveType.Quad);
            var window = Group("SILENCE1 sliding assembly",null);
            SketchProportions(window);
            var frame = Group("Fixed outer frame and tracks",window);
            Profile(frame,"Outer casing",-1.76f,.89f,3.062f,2.12f,.94f,.065f,.073f,ink);
            Profile(frame,"Enamel facing",-1.753f,.897f,3.022f,2.106f,.926f,.048f,.009f,ink);
            Profile(frame,"Inner return",-1.70f,.95f,3.08f,2.0f,.82f,.009f,.100f,edge);
            Shape("Sill lip",frame,new(-.70f,.89f,3.012f),new(2.17f,.017f,.12f),powder);
            foreach (float z in new[]{3.096f,3.144f})
            {
                Shape("Lower rail",frame,new(-.7f,Bottom+.003f,z),new(InnerWidth,.008f,.010f),metal);
                Shape("Upper rail",frame,new(-.7f,Bottom+Height-.003f,z),new(InnerWidth,.008f,.010f),gasket);
            }
            Sash(window,"Rear fixed sash",Left,3.145f,false);
            float closed = Left+InnerWidth-SashWidth;
            var front = Sash(window,"Front sliding sash",(Left+closed)/2,3.094f,true);
            var latch = Group("Crescent handle",front);
            latch.localPosition = new(.020f,Height*.5f,-.019f);
            Shape("Backplate",latch,Vector3.zero,new(.041f,.137f,.011f),metal);
            Shape("Lock pivot",latch,new(.002f,0,-.013f),new(.025f,.031f,.019f),ink);
            Shape("Lever",latch,new(.012f,-.041f,-.029f),new(.028f,.119f,.021f),ink);
            Shape("Index",latch,new(.002f,.031f,-.008f),new(.009f,.009f,.003f),red);
            var pull = Group("Recessed pull",front);
            pull.localPosition = new(SashWidth-.019f,Height*.50f,-.023f);
            Shape("Pull recess",pull,Vector3.zero,new(.022f,.138f,.010f),gasket);
            Shape("Grip",pull,new(-.002f,0,-.008f),new(.009f,.113f,.014f),metal);
            var collider = front.gameObject.AddComponent<BoxCollider>();
            collider.center = new(SashWidth/2,Height/2,-.018f);
            collider.size = new(SashWidth,Height,.085f);
            var control = new GameObject("Window").AddComponent<SlidingWindow>();
            control.roomCamera = camera;
            control.frontSash = front;
            control.gripArea = collider;
            control.crescent = latch;
            control.openX = Left;
            control.closedX = closed;
            control.alleyLow = Source(control.gameObject,"AlleyLow",true);
            control.alleyDetail = Source(control.gameObject,"AlleyDetail",true);
            control.latch = Source(control.gameObject,"CrescentLatch",false);
            WindowSectionBuilder.Attach(control);
            EditorSceneManager.SaveScene(scene,SceneFile);
            EditorBuildSettings.scenes = new[]{new EditorBuildSettingsScene(SceneFile,true)};
            AssetDatabase.SaveAssets();
        }

        [MenuItem("SILENCE1/Build web experience")]
        public static void BuildWeb()
        {
            var dest = Path.GetFullPath(Path.Combine(Application.dataPath,"../../silence1-film/public/experience"));
            PlayerSettings.WebGL.template = "PROJECT:Silence1";
            PlayerSettings.SplashScreen.show = false;
            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Disabled;
            PlayerSettings.WebGL.dataCaching = false;
            // Use the App UI C API archive in Plugins/WebGL; its legacy JS binding object was removed.
            var highlighter = AssetImporter.GetAtPath("Packages/com.unity.dt.app-ui/Runtime/Core/Platform/WebGL/Plugins/libAppUITextMateLib.a") as PluginImporter;
            highlighter?.SetIncludeInBuildDelegate(_ => false);
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions {
                scenes = new[]{SceneFile}, locationPathName = Path.GetFullPath(dest),
                target = BuildTarget.WebGL, options = BuildOptions.None
            });
            if (report.summary.result != BuildResult.Succeeded || !File.Exists(Path.Combine(dest,"index.html")) ||
                !Directory.Exists(Path.Combine(dest,"Build")) || Directory.GetFiles(Path.Combine(dest,"Build"),"*.wasm").Length == 0)
                throw new InvalidOperationException("Web build: "+report.summary.result);
            Debug.Log("SILENCE1 web player built at "+dest);
        }
    }
}
