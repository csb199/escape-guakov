using System;
using UnityEngine;
namespace Game.HotUpdate.Barrack
{
    [CreateAssetMenu(menuName="Barrack/Source UI")] public sealed class BarrackSourceUiDefinition : ScriptableObject
    {
        public TextAsset layout;
        public Sprite[] sprites;
        public Shader uiShader;
        public string[] linearCapturedTextures;
        public Texture2D[] capturedTextures, extraSprites;
        public TextAsset presentationSnapshot;
        public Font[] fonts;
        [Serializable] public sealed class Document { public Node[] nodes; }
        [Serializable] public sealed class Node { public int id,parent; public string name; public bool active,button; public Vector2 anchorMin,anchorMax,pivot,position,size; public Vector3 scale; public Quaternion rotation; public Graphic[] graphics; public Layout[] layouts; }
        [Serializable] public sealed class Graphic { public string kind,text; public Color color; public int sprite,fontSize,alignment,imageType,font,fontStyle,horizontalOverflow,verticalOverflow; public float lineSpacing; public bool preserveAspect; }
        [Serializable] public sealed class Layout { public string kind; public LayoutData data; }
        [Serializable] public sealed class LayoutData { public Padding m_Padding; public int m_ChildAlignment,m_StartCorner,m_StartAxis,m_Constraint,m_ConstraintCount,m_HorizontalFit,m_VerticalFit; public Vector2 m_CellSize; public float m_Spacing,m_MinWidth,m_MinHeight,m_PreferredWidth,m_PreferredHeight,m_FlexibleWidth,m_FlexibleHeight; public bool m_ChildForceExpandWidth,m_ChildForceExpandHeight,m_ChildControlWidth,m_ChildControlHeight,m_IgnoreLayout; }
        [Serializable] public sealed class Padding { public int m_Left,m_Right,m_Top,m_Bottom; }
    }
}
