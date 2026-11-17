using System;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace Game.HotUpdate.Barrack
{
    // Source layout, sprites and fonts; only integrated character and interaction features are shown.
    public sealed class BarrackCalibratedUi
    {
        [Serializable] public sealed class Snapshot { public Character[] roster; public string name,role,level,power; public string[] attributes,linearCapturedTextures; }
        [Serializable] public sealed class Character { public string portrait,power; public bool selected; }
        readonly BarrackSourceUiDefinition assets;
        readonly BarrackPreview host;
        readonly RectTransform root;
        readonly Font medium,bold;
        readonly Color ink=new Color(.83f,.84f,.85f,1), panel=new Color(.09f,.13f,.15f,.88f);
        public Button ReturnButton { get; }
        public BarrackCalibratedUi(BarrackPreview host,Transform parent)
        {
            this.host=host;assets=host.stageDefinition.sourceUi;medium=assets.fonts[0];bold=assets.fonts[1];
            root=new GameObject("Captured PC presentation",typeof(RectTransform)).GetComponent<RectTransform>();root.SetParent(parent,false);
            root.anchorMin=root.anchorMax=root.pivot=new Vector2(0,1);root.sizeDelta=new Vector2(1920,1080);root.localScale=Vector3.one;
            ReturnButton=IconButton("Back",2,32,24,30,36,()=>host.Interaction?.Return());
            Text("Roster title","已载入人形",52,81,220,30,22);
            var list=Rect("Roster clipping",40,116,360,646);list.gameObject.AddComponent<RectMask2D>();
            var hit=list.gameObject.AddComponent<Image>();hit.color=Color.clear;hit.raycastTarget=true;
            var content=Rect("Roster content",0,0,360,Mathf.Max(646,Mathf.CeilToInt(host.characters.Length/3f)*140),list);
            var scroll=list.gameObject.AddComponent<ScrollRect>();scroll.viewport=list;scroll.content=content;
            scroll.horizontal=false;scroll.vertical=true;scroll.movementType=ScrollRect.MovementType.Clamped;scroll.scrollSensitivity=40;
            for(int i=0;i<host.characters.Length;i++)
            {
                int index=i;var entry=host.characters[i];float x=14+(i%3)*116,y=(i/3)*140;
                var cell=Rect("Roster "+entry.id,x,y,104,128,content);
                var card=Image("Card",cell,0,0,104,128,5,new Color(.58f,.6f,.61f,1));card.raycastTarget=true;
                var portraitClip=Rect("Portrait clip",0,0,104,104,cell);portraitClip.gameObject.AddComponent<RectMask2D>();
                var portrait=Rect("Portrait",0,0,104,104,portraitClip).gameObject.AddComponent<RawImage>();portrait.texture=entry.portrait;portrait.raycastTarget=false;
                Image("Name strip",cell,0,104,104,24,5,panel);
                Text("Character name",entry.displayName,0,104,104,24,18,true,TextAnchor.MiddleCenter,cell);
                var selection=Image("Selection",cell,-3,-3,110,134,41,new Color(1,.47f,.12f,1));
                var button=cell.gameObject.AddComponent<Button>();button.targetGraphic=card;button.onClick.AddListener(()=>{_=host.SelectCharacterAsync(index);});
                roster.Add((button,selection.gameObject));
            }
            IconButton("Collapse",6,406,410,20,31,()=>list.gameObject.SetActive(!list.gameObject.activeSelf));
            Box("Character identity",1230,144,314,90,panel);
            nameLabel=Text("Name","",1251,151,270,52,36,true);
            stateLabel=Text("Load state","正在载入整备室…",1251,202,270,27,18);
            Text("Interaction hint","拖动旋转 · 滚轮缩放 · 点击角色互动",520,838,600,30,20);
            Refresh();
        }
        readonly System.Collections.Generic.List<(Button button,GameObject selection)> roster=new System.Collections.Generic.List<(Button,GameObject)>();
        readonly Text nameLabel,stateLabel;
        public void Refresh()
        {
            for(int i=0;i<roster.Count;i++){roster[i].button.interactable=host.Ready&&!host.Switching;roster[i].selection.SetActive(i==host.SelectedCharacter);}
            nameLabel.text=host.CharacterName??"";
            stateLabel.text=host.Failure!=null?"加载失败，请重试":host.Switching?"正在切换人形…":!host.Ready?"正在载入整备室…":"整备室";
            ReturnButton.interactable=host.Ready&&host.Interaction.Mode!=BarrackInteractionMode.Returning;
        }
        RectTransform Rect(string name,float x,float y,float w,float h,Transform parent=null)
        {var r=new GameObject(name,typeof(RectTransform)).GetComponent<RectTransform>();r.SetParent(parent??root,false);r.anchorMin=r.anchorMax=r.pivot=new Vector2(0,1);r.anchoredPosition=new Vector2(x,-y)*1.2f;r.sizeDelta=new Vector2(w,h)*1.2f;return r;}
        Image Image(string name,Transform parent,float x,float y,float w,float h,int sprite,Color color)
        {var image=Rect(name,x,y,w,h,parent).gameObject.AddComponent<Image>();image.sprite=assets.sprites[sprite];image.type=UnityEngine.UI.Image.Type.Sliced;image.color=color;image.raycastTarget=false;return image;}
        void Box(string name,float x,float y,float w,float h,Color color)=>Image(name,root,x,y,w,h,5,color);
        void Icon(string name,int sprite,float x,float y,float w,float h)=>Image(name,root,x,y,w,h,sprite,ink);
        Text Text(string name,string text,float x,float y,float w,float h,int size,bool heavy=false,TextAnchor align=TextAnchor.MiddleLeft,Transform parent=null)
        {var label=Rect(name,x,y,w,h,parent).gameObject.AddComponent<Text>();label.font=heavy?bold:medium;label.text=text;label.fontSize=Mathf.RoundToInt(size*1.2f);label.color=ink;label.alignment=align;label.horizontalOverflow=HorizontalWrapMode.Overflow;label.verticalOverflow=VerticalWrapMode.Overflow;label.raycastTarget=false;return label;}
        void Extra(string name,Transform parent,float x,float y,float w,float h)
        {var texture=assets.extraSprites.FirstOrDefault(t=>t.name==name);if(texture==null)return;var image=Rect(name,x,y,w,h,parent).gameObject.AddComponent<RawImage>();image.texture=texture;image.color=ink;image.raycastTarget=false;}
        void Captured(string name,Transform parent,float x,float y,float w,float h)
        {var texture=assets.capturedTextures.FirstOrDefault(t=>t.name==name);if(texture==null)return;var image=Rect(name,x,y,w,h,parent).gameObject.AddComponent<RawImage>();image.texture=texture;image.uvRect=assets.linearCapturedTextures.Contains(name)?new Rect(0,1,1,-1):new Rect(0,0,1,1);image.raycastTarget=false;}
        Button IconButton(string name,int sprite,float x,float y,float w,float h,UnityEngine.Events.UnityAction action)
        {var image=Image(name,root,x,y,w,h,sprite,ink);image.raycastTarget=true;var button=image.gameObject.AddComponent<Button>();button.targetGraphic=image;button.onClick.AddListener(action);return button;}
    }
}
