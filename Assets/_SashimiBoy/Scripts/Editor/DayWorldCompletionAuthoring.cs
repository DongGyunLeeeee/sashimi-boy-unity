using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace SashimiBoy.EditorTools
{
    public static class DayWorldCompletionAuthoring
    {
        public static void Apply(DayWorldSceneDirector director)
        {
            if (director == null || director.completeRoot == null) return;
            var root = director.completeRoot.GetComponent<RectTransform>();
            root.anchorMin = Vector2.zero; root.anchorMax = Vector2.one;
            root.offsetMin = root.offsetMax = Vector2.zero;
            root.GetComponent<Image>().color = new Color(.025f,.045f,.06f,.97f);
            var screen = root.GetComponent<DayWorldStageClearScreen>() ?? root.gameObject.AddComponent<DayWorldStageClearScreen>();
            screen.title = Label(root,"EndTitle","스테이지 클리어",new Vector2(.5f,.65f),new Vector2(1100,100),52);
            screen.title.color = new Color(.98f,.79f,.41f);
            screen.description = Label(root,"ToBeContinued","",new Vector2(.5f,.54f),new Vector2(1050,85),25);
            screen.continueButton = Button(root,"ContinueStage","이어서 하기",new Vector2(.36f,.38f));
            screen.saveAndExitButton = Button(root,"SaveAndExit","저장하고 나가기",new Vector2(.64f,.38f));
            screen.saveStatus = Label(root,"SaveStatus","",new Vector2(.5f,.26f),new Vector2(1000,75),21);
            Label(root,"StageClearKicker","SASHIMI BOY",new Vector2(.5f,.76f),new Vector2(800,48),24).color = new Color(.57f,.75f,.75f);
            director.stageClearScreen = screen;
            root.gameObject.SetActive(false);
            EditorUtility.SetDirty(director); EditorUtility.SetDirty(screen);
        }
        static RectTransform Rect(Transform parent,string name,Vector2 anchor,Vector2 size)
        {
            var existing=parent.Find(name);
            var rect=existing!=null ? existing.GetComponent<RectTransform>() : new GameObject(name,typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent,false);rect.anchorMin=rect.anchorMax=anchor;rect.anchoredPosition=Vector2.zero;rect.sizeDelta=size;
            return rect;
        }
        static Text Label(Transform parent,string name,string value,Vector2 anchor,Vector2 size,int fontSize)
        {
            var rect=Rect(parent,name,anchor,size);var text=rect.GetComponent<Text>() ?? rect.gameObject.AddComponent<Text>();
            text.font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");text.text=value;text.color=new Color(.89f,.94f,.94f);
            text.fontSize=fontSize;text.alignment=TextAnchor.MiddleCenter;text.raycastTarget=false;
            text.horizontalOverflow=HorizontalWrapMode.Wrap;text.verticalOverflow=VerticalWrapMode.Truncate;return text;
        }
        static Button Button(Transform parent,string name,string title,Vector2 anchor)
        {
            var rect=Rect(parent,name,anchor,new Vector2(380,82));var image=rect.GetComponent<Image>() ?? rect.gameObject.AddComponent<Image>();
            image.color=new Color(.11f,.34f,.37f);var button=rect.GetComponent<Button>() ?? rect.gameObject.AddComponent<Button>();button.targetGraphic=image;
            Label(rect,"Label",title,new Vector2(.5f,.5f),new Vector2(356,66),28);
            return button;
        }
    }
}
