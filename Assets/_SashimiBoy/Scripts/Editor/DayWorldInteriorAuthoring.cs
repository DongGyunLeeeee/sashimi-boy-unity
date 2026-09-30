using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace SashimiBoy.EditorTools
{
    // Owns InteriorShell and entry placement. Existing door components and spawn references survive reapplication.
    public static class DayWorldInteriorAuthoring
    {
        const string Art = DayWorldAssetAuthoring.Output + "/Interiors";
        static readonly string[] Rooms = { "KevinHome", "FishShopDialogue", "EquipmentShop", "Club" };
        static HashSet<string> authoredChildren;

        [MenuItem("Sashimi Boy/Day 01 + Day 02/Apply Interior Walls Only")]
        public static void ApplyBatch()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new System.InvalidOperationException("Exit Play mode before authoring.");
            for (int i = 0; i < SceneManager.sceneCount; i++)
                if (SceneManager.GetSceneAt(i).isDirty) throw new System.InvalidOperationException("Save unsaved scenes first.");
            foreach (string room in Rooms)
            {
                var scene = EditorSceneManager.OpenScene("Assets/_SashimiBoy/Scenes/" + room + ".unity", OpenSceneMode.Single);
                var root = scene.GetRootGameObjects().Single(g => g.name == "DayWorld_Integration").transform;
                Apply(scene, root);
                DayWorldStreetAuthoring.Apply(scene, root);
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
            }
            AssetDatabase.SaveAssets();
            Debug.Log("[DayWorldInterior] Applied four existing interiors; Stage01 and Street scenes were not rewritten.");
        }

        public static void Apply(Scene scene, Transform worldRoot)
        {
            string room = scene.name;
            if (!Rooms.Contains(room)) return;
            if (!AssetDatabase.IsValidFolder(Art)) AssetDatabase.CreateFolder(DayWorldAssetAuthoring.Output, "Interiors");
            var previous = worldRoot.Find("InteriorShell");
            authoredChildren = new HashSet<string>();
            // Keep the original backdrop cubes serialized, with the finished shell taking over rendering/collision.
            string[] oldWalls = room == "KevinHome"
                ? new[] { "BackWall", "LeftWall", "RightWall", "FrontWallLeft", "FrontWallRight" }
                : new[] { "BackWall" };
            foreach (var t in scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true)))
                if (oldWalls.Contains(t.name)) t.gameObject.SetActive(false);
            var shell = previous != null ? previous : new GameObject("InteriorShell").transform;
            shell.SetParent(worldRoot, false);
            bool home = room == "KevinHome", fish = room == "FishShopDialogue", club = room == "Club";
            if (home) DayWorldHomeAuthoring.ApplyRoomLayout(scene, worldRoot);
            float w = home ? DayWorldHomeAuthoring.HalfWidth : fish ? 6f : club ? 8f : 6.5f;
            float d = home ? DayWorldHomeAuthoring.HalfDepth : club ? 6f : 4f;
            float h = home ? DayWorldHomeAuthoring.RoomHeight : fish ? 3.6f : club ? 4.3f : 3.7f;
            var door = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true))
                .Single(t => t.name == "Door_To_Street");
            CenterEntry(scene, door, d);
            float doorX = door.position.x, entryZ = -d;
            float opening = home ? .625f : .75f;
            Color plaster = home ? new Color(.79f,.76f,.66f) : fish ? new Color(.79f,.87f,.85f)
                : club ? new Color(.09f,.065f,.12f) : new Color(.20f,.23f,.25f);
            Color lower = home ? new Color(.34f,.42f,.35f) : fish ? new Color(.13f,.42f,.42f)
                : club ? new Color(.14f,.10f,.18f) : new Color(.37f,.23f,.13f);
            Color wood = home ? new Color(.29f,.20f,.12f) : fish ? new Color(.15f,.25f,.25f)
                : club ? new Color(.025f,.033f,.05f) : new Color(.16f,.10f,.07f);
            var wall = Material(room + "_Wall", plaster, fish ? .35f : .12f);
            var panel = Material(room + "_Lower", lower, fish ? .45f : .15f);
            var trim = Material(room + "_Trim", wood, .2f);
            var ceiling = Material(room + "_Ceiling", home ? new Color(.83f,.80f,.72f)
                : fish ? new Color(.75f,.81f,.80f) : club ? new Color(.045f,.04f,.065f) : new Color(.22f,.21f,.20f), .05f);
            // The facade and interior now share the same centered opening and straight front wall.
            Vector3 a = new Vector3(-w,0,-d);
            Wall(shell,"EntryLeft",a,new Vector3(doorX-opening,0,entryZ),h,wall,panel,trim);
            Wall(shell,"EntryRight",new Vector3(doorX+opening,0,entryZ),new Vector3(w,0,entryZ),h,wall,panel,trim);
            Box(shell,"DoorLintel",new Vector3(doorX,(h+2.12f)*.5f,entryZ),new Vector3(opening*2f,h-2.12f,.18f),wall);
            Wall(shell,"Right",new Vector3(w,0,entryZ),new Vector3(w,0,d),h,wall,panel,trim);
            Wall(shell,"Back",new Vector3(w,0,d),new Vector3(-w,0,d),h,wall,panel,trim);
            Wall(shell,"Left",new Vector3(-w,0,d),a,h,wall,panel,trim);
            Box(shell,"Ceiling",new Vector3(0,h+.09f,0),new Vector3(w*2f+.18f,.18f,d*2f+.18f),ceiling);
            float revealDepth = Mathf.Max(.18f,door.position.z-entryZ+.18f);
            foreach (float sign in new[] {-1f,1f})
                Box(shell,"DoorReveal",new Vector3(doorX+sign*(opening+.035f),1.08f,entryZ+revealDepth*.5f),new Vector3(.07f,2.16f,revealDepth),trim);
            Box(shell,"DoorHeader",new Vector3(doorX,2.12f,entryZ+revealDepth*.5f),new Vector3(opening*2f+.14f,.12f,revealDepth),trim);
            Box(shell,"DoorCrown",new Vector3(doorX,h-.12f,entryZ+.13f),new Vector3(opening*2f,.065f,.065f),trim,false);
            if (!home) Box(shell,"DoorTransom",new Vector3(doorX,1.84f,door.position.z-.04f),new Vector3(1.5f,.48f,.15f),trim);

            if (home)
            {
                HomeWindow(shell,trim);
                foreach(var light in worldRoot.GetComponentsInChildren<Light>())
                    if(light.name=="HomeWarmLight") light.intensity=.50f;
                var plaque=Material("Home_LabelPlaque",new Color(.075f,.11f,.10f),.05f);
                foreach(var label in worldRoot.GetComponentsInChildren<TextMesh>().Where(t=>t.name=="BedLabel" || t.name=="StationName" || t.name=="HomeExit"))
                {
                    Vector3 size=label.GetComponent<Renderer>().bounds.size;
                    var backing=Box(shell,"LabelPlaque",label.transform.position+label.transform.forward*.035f,
                        new Vector3(Mathf.Max(.65f,size.x+.16f),Mathf.Max(.19f,size.y+.08f),.035f),plaque,false);
                    backing.transform.rotation=label.transform.rotation;
                }
                for(int i=0;i<Mathf.CeilToInt(w*2f/.8f);i++) Box(shell,"TimberCeilingJoint",new Vector3(-w+.4f+i*.8f,h-.013f,0),new Vector3(.025f,.028f,d*2f-.2f),trim,false);
            }
            else if (fish)
            {
                var grout=Material("FishShop_Grout",new Color(.50f,.65f,.63f),.1f);
                for(int row=0;row<5;row++) Box(shell,"BackTileGrout",new Vector3(0,1.22f+row*.45f,3.885f),new Vector3(11.7f,.018f,.016f),grout,false);
                for(int col=0;col<20;col++) Box(shell,"BackTileJoint",new Vector3(-5.7f+col*.6f,2.19f,3.885f),new Vector3(.016f,2.28f,.016f),grout,false);
            }
            else if (!club)
            {
                var felt=Material("EquipmentShop_AcousticFelt",new Color(.10f,.12f,.13f),0f);
                for(int i=0;i<12;i++)
                {
                    float x=-5.85f+i*1.06f;
                    Box(shell,"AcousticPanel",new Vector3(x,2.23f,3.82f),new Vector3(.66f,1.75f,.14f),felt,false);
                    Box(shell,"TimberSlat",new Vector3(x+.40f,1.86f,3.83f),new Vector3(.045f,3.30f,.12f),trim,false);
                }
            }
            else
            {
                var cyan=Material("Club_CyanNeon",new Color(.06f,.60f,.70f),.25f,new Color(.05f,.85f,1f)*2f);
                var pink=Material("Club_PinkNeon",new Color(.65f,.05f,.33f),.25f,new Color(1f,.055f,.38f)*2f);
                for(int i=0;i<7;i++)
                {
                    float z=-3.6f+i*1.4f;
                    Box(shell,"LeftNeonRib",new Vector3(-7.82f,2.15f,z),new Vector3(.065f,3.4f,.07f),cyan,false);
                    Box(shell,"RightNeonRib",new Vector3(7.82f,2.15f,z),new Vector3(.065f,3.4f,.07f),pink,false);
                }
                Box(shell,"BackNeonBand",new Vector3(0,3.50f,5.83f),new Vector3(15.6f,.065f,.065f),pink,false);
            }
            RenderSettings.ambientMode=AmbientMode.Flat;
            RenderSettings.ambientLight=home ? new Color(.34f,.32f,.28f) : fish ? new Color(.32f,.36f,.35f)
                : club ? new Color(.16f,.13f,.22f) : new Color(.40f,.37f,.33f);
            Color glow=home ? new Color(1f,.83f,.58f) : fish ? new Color(.84f,1f,.98f)
                : club ? new Color(.37f,.24f,.72f) : new Color(1f,.78f,.48f);
            var diffuser=Material(room+"_Lamp",glow,.2f,glow*(club?1.8f:1.1f));
            foreach(float x in new[]{-w*.5f,w*.5f}) foreach(float z in new[]{-d*.40f,d*.45f})
            {
                Box(shell,"CeilingFixture",new Vector3(x,h-.06f,z),new Vector3(home?.75f:1.4f,.10f,.35f),trim,false);
                Box(shell,"LampDiffuser",new Vector3(x,h-.12f,z),new Vector3(home?.64f:1.28f,.025f,.27f),diffuser,false);
                string lampName=UniqueName(shell,"InteriorFill");
                var existing=shell.Find(lampName);
                var lamp=existing!=null ? existing.GetComponent<Light>() : new GameObject(lampName).AddComponent<Light>();
                lamp.transform.SetParent(shell,false);
                lamp.transform.position=new Vector3(x,h-.32f,z);lamp.type=LightType.Point;lamp.range=club?11f:9f;
                lamp.intensity=home || fish ? .45f : club ? 1.2f : 1.5f;lamp.color=glow;lamp.shadows=LightShadows.None;
            }
            // Remove only obsolete generated wall segments; retained shell objects keep their serialized IDs.
            foreach(var child in shell.Cast<Transform>().ToArray())
                if(!authoredChildren.Contains(child.name)) Object.DestroyImmediate(child.gameObject);
            authoredChildren=null;
        }

        private static void CenterEntry(Scene scene, Transform door, float halfDepth)
        {
            Vector3 target = new Vector3(0f, door.position.y, -halfDepth + .1f);
            Vector3 delta = target - door.position;
            var transforms = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true)).ToArray();
            // These owner home visuals are siblings of the retained interaction collider.
            foreach (var visual in transforms.Where(t => t.name == "HomeDoor" || t.name == "DoorRecess" || t.name == "HomeExit"))
                if (!visual.IsChildOf(door))
                {
                    visual.position += delta;
                    PrefabUtility.RecordPrefabInstancePropertyModifications(visual);
                }
            door.position = target;
            SyncReturnDoorAnchor(scene, door);
            var director = transforms.Select(t => t.GetComponent<DayWorldSceneDirector>()).First(d => d != null);
            var entry = director.FindSpawn("Entry");
            entry.SetPositionAndRotation(new Vector3(0f, entry.position.y, -halfDepth + 1.2f), Quaternion.identity);
            EditorUtility.SetDirty(door); EditorUtility.SetDirty(entry);
        }

        internal static void SyncReturnDoorAnchor(Scene scene, Transform door)
        {
            var anchor = scene.GetRootGameObjects()
                .SelectMany(g => g.GetComponentsInChildren<Transform>(true))
                .SingleOrDefault(t => t.name == "ReturnDoor_Anchor");
            if (anchor == null) return;
            anchor.SetPositionAndRotation(door.position, door.rotation);
            EditorUtility.SetDirty(anchor);
        }

        static void Wall(Transform parent,string name,Vector3 start,Vector3 end,float height,Material wall,Material panel,Material trim)
        {
            Vector3 delta=end-start;
            if(delta.sqrMagnitude<.001f)return;
            float length=delta.magnitude;
            Vector3 inward=new Vector3(-delta.z,0,delta.x).normalized;
            Quaternion rotation=Quaternion.Euler(0,-Mathf.Atan2(delta.z,delta.x)*Mathf.Rad2Deg,0);
            Vector3 center=(start+end)*.5f;
            Box(parent,"Wall_"+name,center+Vector3.up*(height*.5f),new Vector3(length,height,.18f),wall).transform.rotation=rotation;
            Box(parent,"Wainscot_"+name,center+inward*.108f+Vector3.up*.54f,new Vector3(length,1.05f,.035f),panel,false).transform.rotation=rotation;
            foreach(float y in new[]{.10f,1.065f,height-.12f})
                Box(parent,"Trim_"+name,center+inward*.13f+Vector3.up*y,new Vector3(length,.065f,.065f),trim,false).transform.rotation=rotation;
        }
        static void HomeWindow(Transform root,Material wood)
        {
            var sky=Material("Home_WindowGlass",new Color(.40f,.58f,.62f),.65f,new Color(.28f,.41f,.43f)*.45f);
            var linen=Material("Home_Curtain",new Color(.63f,.51f,.35f),0f);
            Box(root,"WindowFrame",new Vector3(-.25f,1.90f,DayWorldHomeAuthoring.HalfDepth-.14f),new Vector3(1.25f,.90f,.13f),wood,false);
            Box(root,"WindowGlass",new Vector3(-.25f,1.90f,DayWorldHomeAuthoring.HalfDepth-.22f),new Vector3(1.10f,.76f,.025f),sky,false);
            Box(root,"WindowMullion",new Vector3(-.25f,1.90f,DayWorldHomeAuthoring.HalfDepth-.25f),new Vector3(.045f,.76f,.05f),wood,false);
            Box(root,"WindowCrossbar",new Vector3(-.25f,1.90f,DayWorldHomeAuthoring.HalfDepth-.25f),new Vector3(1.10f,.045f,.05f),wood,false);
            foreach(float x in new[]{-1.02f,.52f}) Box(root,"LinenCurtain",new Vector3(x,1.90f,DayWorldHomeAuthoring.HalfDepth-.27f),new Vector3(.28f,1.05f,.08f),linen,false);
            Box(root,"CurtainRail",new Vector3(-.25f,2.43f,DayWorldHomeAuthoring.HalfDepth-.27f),new Vector3(1.9f,.065f,.07f),wood,false);
        }
        static GameObject Box(Transform parent,string name,Vector3 position,Vector3 size,Material material,bool solid=true)
        {
            string unique=UniqueName(parent,name);
            var previous=parent.Find(unique);
            var go=previous!=null ? previous.gameObject : GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name=unique;go.SetActive(true);go.transform.SetParent(parent,false);go.transform.rotation=Quaternion.identity;
            go.transform.position=position;go.transform.localScale=size;go.GetComponent<Renderer>().sharedMaterial=material;
            var collider=go.GetComponent<Collider>();
            if(!solid && collider!=null) Object.DestroyImmediate(collider);
            else if(solid && collider==null) go.AddComponent<BoxCollider>();
            return go;
        }
        static string UniqueName(Transform parent,string name)
        {
            // The existing club art preservation check keys every transform by its full path.
            string candidate=name;int suffix=1;
            while(!authoredChildren.Add(candidate))candidate=name+"_"+suffix++;
            return candidate;
        }
        static Material Material(string name,Color color,float smoothness,Color emission=default)
        {
            string path=Art+"/MAT_"+name+".mat";
            var material=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(material==null){material=new Material(Shader.Find("Standard"));AssetDatabase.CreateAsset(material,path);}
            material.color=color;material.SetFloat("_Glossiness",smoothness);
            // Retain Unity-normalized keywords when reapplying the same existing colour.
            if(emission.maxColorComponent>0f)
            {
                if(material.GetColor("_EmissionColor")!=emission) material.EnableKeyword("_EMISSION");
                material.SetColor("_EmissionColor",emission);
            }
            EditorUtility.SetDirty(material);return material;
        }
    }
}
