using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
namespace Game.HotUpdate.Barrack
{
    public sealed class BarrackSourceUi : MonoBehaviour
    {
        readonly List<GameObject> nodes=new List<GameObject>();
        BarrackPreview host;
        BarrackCalibratedUi presentation;
        void Update() => presentation?.Refresh();
        Material uiMaterial, fontMaterial;
        void OnDestroy() { if(uiMaterial!=null)Destroy(uiMaterial);if(fontMaterial!=null)Destroy(fontMaterial); }
        public int SourceNodeCount => nodes.Count;
        public Button ReturnButton { get; private set; }
        static bool IsTemplate(string value) => value=="TEXT"||value=="Text"||value=="Duty"||value=="Title"||value=="Title Name"||value=="TEXTWAYS"||value=="TextTextTextText"||value=="Lv.00"||value=="00"||value=="000"||value=="00000"||value=="00/00";
        public void Build(BarrackPreview owner, Transform parent, Font font)
        {
            host=owner;var source=host.stageDefinition.sourceUi;var data=JsonUtility.FromJson<BarrackSourceUiDefinition.Document>(source.layout.text);
            foreach(var n in data.nodes)
            {
                var go=new GameObject(n.name,typeof(RectTransform));var r=go.GetComponent<RectTransform>();r.SetParent(n.parent<0?parent:nodes[n.parent].transform,false);nodes.Add(go);
                r.anchorMin=n.anchorMin;r.anchorMax=n.anchorMax;r.pivot=n.pivot;r.anchoredPosition=n.position;r.sizeDelta=n.size;r.localScale=n.scale;r.localRotation=n.rotation;
                foreach(var g in n.graphics)
                {
                    if(g.kind=="Image") {var image=go.AddComponent<Image>();image.color=g.color;image.raycastTarget=false;if(g.sprite>=0)image.sprite=source.sprites[g.sprite];image.type=(Image.Type)g.imageType;image.preserveAspect=g.preserveAspect; if(image.sprite==null&&image.color.a>0) image.enabled=false;}
                    if(g.kind=="Text") {var text=go.AddComponent<Text>();text.font=g.font>=0 && g.font<source.fonts.Length?source.fonts[g.font]:font; text.fontStyle=(FontStyle)g.fontStyle;text.lineSpacing=g.lineSpacing;text.fontSize=g.fontSize;text.color=g.color;text.alignment=(TextAnchor)g.alignment;text.raycastTarget=false;text.text=IsTemplate(g.text)?"":g.text;text.horizontalOverflow=(HorizontalWrapMode)g.horizontalOverflow;text.verticalOverflow=(VerticalWrapMode)g.verticalOverflow;}
                }
                foreach(var l in n.layouts)
                {
                    var d=l.data;
                    if(l.kind=="HorizontalLayoutGroup"||l.kind=="VerticalLayoutGroup")
                    {
                        var group=l.kind=="HorizontalLayoutGroup"?(HorizontalOrVerticalLayoutGroup)go.AddComponent<HorizontalLayoutGroup>():go.AddComponent<VerticalLayoutGroup>();
                        group.spacing=d.m_Spacing;group.childAlignment=(TextAnchor)d.m_ChildAlignment;group.childControlWidth=d.m_ChildControlWidth;group.childControlHeight=d.m_ChildControlHeight;group.childForceExpandWidth=d.m_ChildForceExpandWidth;group.childForceExpandHeight=d.m_ChildForceExpandHeight;
                        if(d.m_Padding!=null)group.padding=new RectOffset(d.m_Padding.m_Left,d.m_Padding.m_Right,d.m_Padding.m_Top,d.m_Padding.m_Bottom);
                    }
                    if(l.kind=="LayoutElement") {var element=go.AddComponent<LayoutElement>();element.ignoreLayout=d.m_IgnoreLayout;element.minWidth=d.m_MinWidth;element.minHeight=d.m_MinHeight;element.preferredWidth=d.m_PreferredWidth;element.preferredHeight=d.m_PreferredHeight;element.flexibleWidth=d.m_FlexibleWidth;element.flexibleHeight=d.m_FlexibleHeight;}
                    if(l.kind=="ContentSizeFitter") {var fitter=go.AddComponent<ContentSizeFitter>();fitter.horizontalFit=(ContentSizeFitter.FitMode)d.m_HorizontalFit;fitter.verticalFit=(ContentSizeFitter.FitMode)d.m_VerticalFit;}
                }
                go.SetActive(n.active);
            }
            nodes[0].GetComponent<RectTransform>().offsetMin=Vector2.zero;nodes[0].GetComponent<RectTransform>().offsetMax=Vector2.zero;
            foreach(var go in nodes)
            {
                if(go.name=="Text_ChrName")go.GetComponent<Text>().text="闪电";
                if(go.name=="Trans_GrpVisual"||go.name=="GrpAction_PS")go.SetActive(false);
                if(go.name=="BtnBack") {var image=go.GetComponent<Image>()??go.AddComponent<Image>();image.color=Color.clear;image.raycastTarget=true;var button=go.AddComponent<Button>();button.targetGraphic=image;button.onClick.AddListener(()=>host.Interaction?.Return()); if(ReturnButton==null)ReturnButton=button;}
            }
            if(source.presentationSnapshot!=null)
            {
                foreach(var go in nodes)if(go.transform.parent==parent)go.SetActive(false);
                presentation=new BarrackCalibratedUi(host,parent);ReturnButton=presentation.ReturnButton;
                uiMaterial=new Material(source.uiShader) { name="Barrack Unity 6 UI adapter" };
                fontMaterial=new Material(uiMaterial);fontMaterial.SetFloat("_FontMode",1);
                foreach(var graphic in parent.GetComponentsInChildren<Graphic>(true))graphic.material=graphic is Text?fontMaterial:uiMaterial;
            }
        }
    }
}
