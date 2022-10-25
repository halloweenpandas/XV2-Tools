﻿using LB_Common.Numbers;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using Xv2CoreLib.EMB_CLASS;
using Xv2CoreLib.EMM;
using Xv2CoreLib.EMP_NEW.Keyframes;
using Xv2CoreLib.HslColor;
using Xv2CoreLib.Resource;
using Xv2CoreLib.Resource.UndoRedo;

namespace Xv2CoreLib.EMP_NEW
{
    public enum VersionEnum : ushort
    {
        DBXV2 = 37568,
        SDBH = 37632
    }
    public enum ParticleNodeType : byte
    {
        Null = 0,
        Emitter = 1,
        Emission = 2
    }

    public enum ParticleAutoRotationType : ushort
    {
        Camera = 0,
        Front = 1,
        None = 2
    }

    [Serializable]
    public class EMP_File
    {
        public const int EMP_SIGNATURE = 1347241251;

        public VersionEnum Version { get; set; } = VersionEnum.DBXV2;
        /// <summary>
        /// Full Decompile will decompile keyframed values into a more edit-friendly state. Should only be enabled for editor tools (EEPK Organiser / XenoKit), and disabled for the installer. 
        /// This can only be set when initially loading the file.
        /// </summary>
        internal bool FullDecompile { get; set; }

        public AsyncObservableCollection<ParticleNode> ParticleNodes { get; set; } = new AsyncObservableCollection<ParticleNode>();
        public AsyncObservableCollection<EMP_TextureSamplerDef> Textures { get; set; } = new AsyncObservableCollection<EMP_TextureSamplerDef>();

        #region LoadSave
        public byte[] SaveToBytes()
        {
            return new Deserializer(this).bytes.ToArray();
        }

        public static EMP_File Load(string path, bool fullDecompile)
        {
            return new Parser(path, fullDecompile).EmpFile;
        }

        public static EMP_File Load(byte[] bytes, bool fullDecompile)
        {
            return new Parser(bytes, fullDecompile).EmpFile;
        }

        #endregion

        #region References
        /// <summary>
        /// Parses all ParticleEffects and removes the specified Texture ref, if found.
        /// </summary>
        public void RemoveTextureReferences(EMP_TextureSamplerDef textureRef, List<IUndoRedo> undos = null)
        {
            if (ParticleNodes != null)
            {
                RemoveTextureReferences_Recursive(ParticleNodes, textureRef, undos);
            }
        }

        private void RemoveTextureReferences_Recursive(AsyncObservableCollection<ParticleNode> children, EMP_TextureSamplerDef textureRef, List<IUndoRedo> undos = null)
        {
            for (int i = 0; i < children.Count; i++)
            {
                if (children[i].ChildParticleNodes != null)
                {
                    RemoveTextureReferences_Recursive(children[i].ChildParticleNodes, textureRef, undos);
                }

                if (children[i].NodeType == ParticleNodeType.Emission)
                {
                startPoint:
                    foreach (var e in children[i].EmissionNode.Texture.TextureEntryRef)
                    {
                        if (e.TextureRef == textureRef)
                        {
                            if (undos != null)
                                undos.Add(new UndoableListRemove<TextureEntry_Ref>(children[i].EmissionNode.Texture.TextureEntryRef, e));

                            children[i].EmissionNode.Texture.TextureEntryRef.Remove(e);
                            goto startPoint;
                        }
                    }
                }
            }
        }

        public void RefactorTextureRef(EMP_TextureSamplerDef oldTextureRef, EMP_TextureSamplerDef newTextureRef, List<IUndoRedo> undos)
        {
            if (ParticleNodes != null)
            {
                RemoveTextureReferences_Recursive(ParticleNodes, oldTextureRef, newTextureRef, undos);
            }
        }

        private void RemoveTextureReferences_Recursive(AsyncObservableCollection<ParticleNode> children, EMP_TextureSamplerDef oldTextureRef, EMP_TextureSamplerDef newTextureRef, List<IUndoRedo> undos)
        {
            for (int i = 0; i < children.Count; i++)
            {
                if (children[i].ChildParticleNodes != null)
                {
                    RemoveTextureReferences_Recursive(children[i].ChildParticleNodes, oldTextureRef, newTextureRef, undos);
                }

                if (children[i].NodeType == ParticleNodeType.Emission)
                {
                    foreach (var e in children[i].EmissionNode.Texture.TextureEntryRef)
                    {
                        if (e.TextureRef == oldTextureRef)
                        {
                            undos.Add(new UndoableProperty<TextureEntry_Ref>(nameof(e.TextureRef), e, e.TextureRef, newTextureRef));
                            e.TextureRef = newTextureRef;
                        }
                    }
                }
            }
        }

        #endregion

        #region AddRemove
        /// <summary>
        /// Add a new ParticleEffect entry.
        /// </summary>
        /// <param name="index">Where in the collection to insert the new entry. The default value of -1 will result in it being added to the end, as will out of range values.</param>
        public void AddNew(int index = -1, List<IUndoRedo> undos = null)
        {
            if (index < -1 || index > ParticleNodes.Count() - 1) index = -1;

            var newEffect = ParticleNode.GetNew();

            if (index == -1)
            {
                ParticleNodes.Add(newEffect);

                if (undos != null)
                    undos.Add(new UndoableListAdd<ParticleNode>(ParticleNodes, newEffect));
            }
            else
            {
                ParticleNodes.Insert(index, newEffect);

                if (undos != null)
                    undos.Add(new UndoableStateChange<ParticleNode>(ParticleNodes, index, ParticleNodes[index], newEffect));
            }
        }

        public bool RemoveParticleEffect(ParticleNode effectToRemove, List<IUndoRedo> undos = null)
        {
            bool result = false;
            for (int i = 0; i < ParticleNodes.Count; i++)
            {
                result = RemoveInChildren(ParticleNodes[i].ChildParticleNodes, effectToRemove, undos);

                if (ParticleNodes[i] == effectToRemove)
                {
                    if (undos != null)
                        undos.Add(new UndoableListRemove<ParticleNode>(ParticleNodes, effectToRemove));

                    ParticleNodes.Remove(effectToRemove);
                    return true;
                }

                if (result == true)
                {
                    break;
                }
            }

            return result;
        }

        private bool RemoveInChildren(AsyncObservableCollection<ParticleNode> children, ParticleNode effectToRemove, List<IUndoRedo> undos = null)
        {
            for (int i = 0; i < children.Count; i++)
            {
                if (children[i].ChildParticleNodes.Count > 0)
                {
                    RemoveInChildren(children[i].ChildParticleNodes, effectToRemove, undos);
                }

                if (children[i] == effectToRemove)
                {
                    if (undos != null)
                        undos.Add(new UndoableListRemove<ParticleNode>(children, effectToRemove));

                    children.Remove(effectToRemove);
                    return true;
                }

            }

            return false;
        }

        #endregion

        #region Get
        public AsyncObservableCollection<ParticleNode> GetParentList(ParticleNode particleEffect)
        {
            foreach (var e in ParticleNodes)
            {
                AsyncObservableCollection<ParticleNode> result = null;

                if (e.ChildParticleNodes.Count > 0)
                {
                    result = GetParentList_Recursive(e.ChildParticleNodes, particleEffect);
                }
                if (result != null)
                {
                    return result;
                }

                if (e == particleEffect)
                {
                    return ParticleNodes;
                }
            }

            return null;
        }

        private AsyncObservableCollection<ParticleNode> GetParentList_Recursive(AsyncObservableCollection<ParticleNode> children, ParticleNode particleEffect)
        {
            AsyncObservableCollection<ParticleNode> result = null;

            for (int i = 0; i < children.Count; i++)
            {
                if (children[i].ChildParticleNodes.Count > 0)
                {
                    result = GetParentList_Recursive(children[i].ChildParticleNodes, particleEffect);
                }

                if (children[i] == particleEffect)
                {
                    return children;
                }
            }

            if (result != null)
            {
                return result;
            }
            else
            {
                return null;
            }
        }

        public List<EMP_TextureSamplerDef> GetTextureEntriesThatUseRef(EMB_CLASS.EmbEntry textureRef)
        {
            List<EMP_TextureSamplerDef> textures = new List<EMP_TextureSamplerDef>();

            foreach (var texture in Textures)
            {
                if (texture.TextureRef == textureRef)
                {
                    textures.Add(texture);
                }
            }

            return textures;
        }

        public List<ParticleTexture> GetTexturePartsThatUseMaterialRef(EmmMaterial materialRef)
        {
            List<ParticleTexture> textureParts = new List<ParticleTexture>();

            foreach (var particleEffect in ParticleNodes)
            {
                if (particleEffect.EmissionNode.Texture.MaterialRef == materialRef)
                {
                    textureParts.Add(particleEffect.EmissionNode.Texture);
                }

                if (particleEffect.ChildParticleNodes != null)
                {
                    textureParts = GetTexturePartsThatUseMaterialRef_Recursive(materialRef, textureParts, particleEffect.ChildParticleNodes);
                }

            }

            return textureParts;
        }

        private List<ParticleTexture> GetTexturePartsThatUseMaterialRef_Recursive(EmmMaterial materialRef, List<ParticleTexture> textureParts, AsyncObservableCollection<ParticleNode> particleEffects)
        {
            foreach (var particleEffect in particleEffects)
            {
                if (particleEffect.EmissionNode.Texture.MaterialRef == materialRef)
                {
                    textureParts.Add(particleEffect.EmissionNode.Texture);
                }

                if (particleEffect.ChildParticleNodes != null)
                {
                    textureParts = GetTexturePartsThatUseMaterialRef_Recursive(materialRef, textureParts, particleEffect.ChildParticleNodes);
                }

            }

            return textureParts;
        }

        public List<ParticleTexture> GetTexturePartsThatUseEmbEntryRef(EMP_TextureSamplerDef embEntryRef)
        {
            List<ParticleTexture> textureParts = new List<ParticleTexture>();

            foreach (var particleEffect in ParticleNodes)
            {
                foreach (var textureEntry in particleEffect.EmissionNode.Texture.TextureEntryRef)
                {
                    if (textureEntry.TextureRef == embEntryRef)
                    {
                        textureParts.Add(particleEffect.EmissionNode.Texture);
                        break;
                    }
                }

                if (particleEffect.ChildParticleNodes != null)
                {
                    textureParts = GetTexturePartsThatUseEmbEntryRef_Recursive(embEntryRef, textureParts, particleEffect.ChildParticleNodes);
                }

            }

            return textureParts;
        }

        private List<ParticleTexture> GetTexturePartsThatUseEmbEntryRef_Recursive(EMP_TextureSamplerDef embEntryRef, List<ParticleTexture> textureParts, AsyncObservableCollection<ParticleNode> particleEffects)
        {
            foreach (var particleEffect in particleEffects)
            {
                foreach (var textureEntry in particleEffect.EmissionNode.Texture.TextureEntryRef)
                {
                    if (textureEntry.TextureRef == embEntryRef)
                    {
                        textureParts.Add(particleEffect.EmissionNode.Texture);
                        break;
                    }
                }

                if (particleEffect.ChildParticleNodes != null)
                {
                    textureParts = GetTexturePartsThatUseEmbEntryRef_Recursive(embEntryRef, textureParts, particleEffect.ChildParticleNodes);
                }

            }

            return textureParts;
        }

        /// <summary>
        /// Finds an identical texture. Returns null if none exists.
        /// </summary>
        /// <param name="texture"></param>
        /// <returns></returns>
        public EMP_TextureSamplerDef GetTexture(EMP_TextureSamplerDef texture)
        {
            if (texture == null) return null;

            foreach (var tex in Textures)
            {
                if (tex.Compare(texture)) return tex;
            }

            return null;
        }

        #endregion

        #region Color

        public List<RgbColor> GetUsedColors()
        {
            List<RgbColor> colors = new List<RgbColor>();

            foreach (var particleEffect in ParticleNodes)
            {
                colors.AddRange(particleEffect.GetUsedColors());
            }

            return colors;
        }

        public void ChangeHue(double hue, double saturation, double lightness, List<IUndoRedo> undos = null, bool hueSet = false, int variance = 0)
        {
            if (ParticleNodes == null) return;
            if (undos == null) undos = new List<IUndoRedo>();

            foreach (var particleEffects in ParticleNodes)
            {
                particleEffects.ChangeHue(hue, saturation, lightness, undos, hueSet, variance);
            }
        }

        public List<IUndoRedo> RemoveColorAnimations(AsyncObservableCollection<ParticleNode> particleEffects = null, bool root = true)
        {
            if (particleEffects == null && root) particleEffects = ParticleNodes;
            if (particleEffects == null && !root) return new List<IUndoRedo>();

            List<IUndoRedo> undos = new List<IUndoRedo>();

            foreach (var particleEffect in particleEffects)
            {
                undos.AddRange(particleEffect.EmissionNode.Texture.Color1.RemoveAllKeyframes());
                undos.AddRange(particleEffect.EmissionNode.Texture.Color2.RemoveAllKeyframes());

                if (particleEffect.ChildParticleNodes != null)
                    undos.AddRange(RemoveColorAnimations(particleEffect.ChildParticleNodes, false));
            }

            return undos;
        }

        public List<IUndoRedo> RemoveRandomColorRange(AsyncObservableCollection<ParticleNode> particleEffects = null, bool root = true)
        {
            if (particleEffects == null && root) particleEffects = ParticleNodes;
            if (particleEffects == null && !root) return new List<IUndoRedo>();

            List<IUndoRedo> undos = new List<IUndoRedo>();

            foreach (var particleEffect in particleEffects)
            {
                particleEffect.RemoveColorRandomRange(undos);

                if (particleEffect.ChildParticleNodes != null)
                    undos.AddRange(RemoveRandomColorRange(particleEffect.ChildParticleNodes, false));
            }

            return undos;
        }

        #endregion

        public EMP_File Clone()
        {
            AsyncObservableCollection<ParticleNode> _ParticleEffects = new AsyncObservableCollection<ParticleNode>();
            AsyncObservableCollection<EMP_TextureSamplerDef> _Textures = new AsyncObservableCollection<EMP_TextureSamplerDef>();
            foreach (var e in ParticleNodes)
            {
                _ParticleEffects.Add(e.Clone());
            }
            foreach (var e in Textures)
            {
                _Textures.Add(e.Clone());
            }

            return new EMP_File()
            {
                ParticleNodes = _ParticleEffects,
                Textures = _Textures
            };
        }

        //DEBUG:
        public List<ParticleNode> GetAllParticleEffects_DEBUG()
        {
            List<ParticleNode> GetAllParticleEffectsRecursive_DEBUG(IList<ParticleNode> particleEffects)
            {
                List<ParticleNode> total = new List<ParticleNode>();

                foreach (var particle in particleEffects)
                {
                    total.Add(particle);

                    if (particle.ChildParticleNodes != null)
                    {
                        total.AddRange(GetAllParticleEffectsRecursive_DEBUG(particle.ChildParticleNodes));
                    }
                }

                return total;
            }

            return GetAllParticleEffectsRecursive_DEBUG(ParticleNodes);
        }
    }

    [Serializable]
    public class ParticleNode : INotifyPropertyChanged
    {
        public const int ENTRY_SIZE = 160;

        #region UI
        [field: NonSerialized]
        public event PropertyChangedEventHandler PropertyChanged;

        public void NotifyPropertyChanged(string propertyName = "")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        [NonSerialized]
        private KeyframedBaseValue _selectedKeyframedValue = null;
        public KeyframedBaseValue SelectedKeyframedValue
        {
            get => _selectedKeyframedValue;
            set
            {
                if(_selectedKeyframedValue != value)
                {
                    SelectedKeyframedValue = value;
                    NotifyPropertyChanged(nameof(SelectedKeyframedValue));
                }
            }
        }

        #endregion

        private string _name = null;
        public string Name
        {
            get => _name;
            set
            {
                if (value != _name)
                {
                    _name = value;
                    NotifyPropertyChanged(nameof(Name));
                }
            }
        }

        public ParticleNodeType NodeType { get; set; }
        /// <summary>
        /// Returns the old "ComponentType" that was used in the old EMP parser, and are how the node types are saved to binary. Read-only.
        /// </summary>
        public NodeSpecificType NodeSpecificType
        {
            get
            {
                switch (NodeType)
                {
                    case ParticleNodeType.Null:
                        return NodeSpecificType.Null;
                    case ParticleNodeType.Emission:
                        return EmissionNode.GetNodeType();
                    case ParticleNodeType.Emitter:
                        return EmitterNode.GetNodeType();
                }

                return NodeSpecificType.Null;
            }
        }

        public byte StartTime { get; set; } //I_44
        public byte StartTime_Variance { get; set; } //I_45
        public bool Loop { get; set; } //I_32_1
        public bool FlashOnGeneration { get; set; } //I_32_3
        public short MaxInstances { get; set; } //I_38
        public ushort Lifetime { get; set; } //I_40
        public ushort Lifetime_Variance { get; set; } //I_42
        public bool Hide { get; set; } //I_33_3
        public bool UseScaleXY { get; set; } //I_33_4
        public bool UseColor2 { get; set; } //I_33_6
        public ParticleAutoRotationType AutoRotationType { get; set; } //int8, I_35
        public byte BurstFrequency { get; set; } //I_46
        public byte BurstFrequency_Variance { get; set; } //I_47
        public ushort Burst { get; set; }
        public ushort Burst_Variance { get; set; } //I_54

        public KeyframedVector3Value Position { get; set; } = new KeyframedVector3Value(0, 0, 0, KeyframedValueType.Position);
        public KeyframedVector3Value Rotation { get; set; } = new KeyframedVector3Value(0, 0, 0, KeyframedValueType.Rotation);
        public CustomVector4 Position_Variance { get; set; } = new CustomVector4();
        public CustomVector4 Rotation_Variance { get; set; } = new CustomVector4();
        public bool EnableRandomRotationDirection { get; set; } //I_34_4
        public bool EnableRandomUpVectorOnVirtualCone { get; set; } //I_34_6
        public float F_128 { get; set; }
        public float F_132 { get; set; }
        public ushort I_136 { get; set; }

        //Unknown values
        public bool I_32_0 { get; set; }
        public bool I_32_2 { get; set; }
        public bool I_32_4 { get; set; }
        public bool I_32_5 { get; set; }
        public bool I_32_6 { get; set; }
        public bool I_32_7 { get; set; }
        public bool I_33_0 { get; set; }
        public bool I_33_1 { get; set; }
        public bool I_33_2 { get; set; }
        public bool I_33_5 { get; set; }
        public bool I_33_7 { get; set; }
        public bool I_34_0 { get; set; }
        public bool I_34_1 { get; set; }
        public bool I_34_2 { get; set; }
        public bool I_34_3 { get; set; }
        public bool I_34_5 { get; set; }
        public bool I_34_7 { get; set; }
        public ushort I_48 { get; set; }
        public ushort I_50 { get; set; }
        public ushort I_56 { get; set; }
        public ushort I_58 { get; set; }
        public ushort I_60 { get; set; }
        public ushort I_62 { get; set; }

        public ParticleEmitter EmitterNode { get; set; } = new ParticleEmitter();
        public ParticleEmission EmissionNode { get; set; } = new ParticleEmission();

        public AsyncObservableCollection<EMP_KeyframedValue> KeyframedValues { get; set; } = new AsyncObservableCollection<EMP_KeyframedValue>();
        public AsyncObservableCollection<EMP_KeyframeGroup> GroupKeyframedValues { get; set; } = new AsyncObservableCollection<EMP_KeyframeGroup>();
        public AsyncObservableCollection<ParticleNode> ChildParticleNodes { get; set; } = new AsyncObservableCollection<ParticleNode>();


        public ParticleNode Clone(bool ignoreChildren = false)
        {
            AsyncObservableCollection<ParticleNode> _children = new AsyncObservableCollection<ParticleNode>();

            if (!ignoreChildren && ChildParticleNodes != null)
            {
                foreach (var e in ChildParticleNodes)
                {
                    _children.Add(e.Clone());
                }
            }

            return new ParticleNode()
            {
                I_136 = I_136,
                I_32_0 = I_32_0,
                Loop = Loop,
                I_32_2 = I_32_2,
                FlashOnGeneration = FlashOnGeneration,
                I_32_4 = I_32_4,
                I_32_5 = I_32_5,
                I_32_6 = I_32_6,
                I_32_7 = I_32_7,
                I_33_0 = I_33_0,
                I_33_1 = I_33_1,
                I_33_2 = I_33_2,
                Hide = Hide,
                UseScaleXY = UseScaleXY,
                I_33_5 = I_33_5,
                UseColor2 = UseColor2,
                I_33_7 = I_33_7,
                I_34_0 = I_34_0,
                I_34_1 = I_34_1,
                I_34_2 = I_34_2,
                I_34_3 = I_34_3,
                EnableRandomRotationDirection = EnableRandomRotationDirection,
                I_34_5 = I_34_5,
                EnableRandomUpVectorOnVirtualCone = EnableRandomUpVectorOnVirtualCone,
                I_34_7 = I_34_7,
                AutoRotationType = AutoRotationType,
                MaxInstances = MaxInstances,
                Lifetime = Lifetime,
                Lifetime_Variance = Lifetime_Variance,
                StartTime = StartTime,
                StartTime_Variance = StartTime_Variance,
                BurstFrequency = BurstFrequency,
                BurstFrequency_Variance = BurstFrequency_Variance,
                I_48 = I_48,
                I_50 = I_50,
                Burst = Burst,
                Burst_Variance = Burst_Variance,
                I_56 = I_56,
                I_58 = I_58,
                I_60 = I_60,
                I_62 = I_62,
                Rotation = Rotation.Copy(),
                Rotation_Variance = Rotation_Variance.Copy(),
                F_128 = F_128,
                F_132 = F_132,
                Position = Position.Copy(),
                Position_Variance = Position_Variance.Copy(),
                Name = Utils.CloneString(Name),
                KeyframedValues = KeyframedValues.Copy(),
                GroupKeyframedValues = GroupKeyframedValues.Copy(),
                EmissionNode = EmissionNode.Clone(),
                EmitterNode = EmitterNode.Copy(),
                NodeType = NodeType,
                ChildParticleNodes = _children
            };
        }

        public static ParticleNode GetNew()
        {
            return new ParticleNode()
            {
                Name = "New Node",
                NodeType = ParticleNodeType.Null,
                AutoRotationType = ParticleAutoRotationType.Camera
            };
        }

        /// <summary>
        /// Add a new ParticleEffect entry.
        /// </summary>
        /// <param name="index">Where in the collection to insert the new entry. The default value of -1 will result in it being added to the end.</param>
        public void AddNew(int index = -1, List<IUndoRedo> undos = null)
        {
            if (index < -1 || index > ChildParticleNodes.Count() - 1) index = -1;

            var newEffect = GetNew();

            if (index == -1)
            {
                ChildParticleNodes.Add(newEffect);

                if (undos != null)
                    undos.Add(new UndoableListAdd<ParticleNode>(ChildParticleNodes, newEffect));
            }
            else
            {
                ChildParticleNodes.Insert(index, newEffect);

                if (undos != null)
                    undos.Add(new UndoableStateChange<ParticleNode>(ChildParticleNodes, index, ChildParticleNodes[index], newEffect));
            }
        }

        public void CopyValues(ParticleNode particleEffect, List<IUndoRedo> undos = null)
        {
            if (undos == null) undos = new List<IUndoRedo>();

            undos.AddRange(Utils.CopyValues(this, particleEffect));

            var emitter = particleEffect.EmitterNode.Copy();
            var emission = particleEffect.EmissionNode.Clone(); //Clone keeps texture and material references intact
            var type_0 = particleEffect.KeyframedValues.Copy();
            var type_1 = particleEffect.GroupKeyframedValues.Copy();

            undos.Add(new UndoableProperty<ParticleNode>(nameof(EmitterNode), this, EmitterNode, emitter));
            undos.Add(new UndoableProperty<ParticleNode>(nameof(EmissionNode), this, EmissionNode, emission));
            undos.Add(new UndoableProperty<ParticleNode>(nameof(KeyframedValues), this, KeyframedValues, type_0));
            undos.Add(new UndoableProperty<ParticleNode>(nameof(GroupKeyframedValues), this, GroupKeyframedValues, type_1));
            undos.Add(new UndoActionPropNotify(this, true));

            EmitterNode = emitter;
            EmissionNode = emission;
            KeyframedValues = type_0;
            GroupKeyframedValues = type_1;

            this.NotifyPropsChanged();
        }

        public List<RgbColor> GetUsedColors()
        {
            List<RgbColor> colors = new List<RgbColor>();

            RgbColor color1 = EmissionNode.Texture.Color1.GetAverageColor();
            RgbColor color2 = EmissionNode.Texture.Color2.GetAverageColor();

            if (!color1.IsWhiteOrBlack)
                colors.Add(color1);

            if (!color2.IsWhiteOrBlack)
                colors.Add(color2);


            if (ChildParticleNodes != null)
            {
                foreach(var child in ChildParticleNodes)
                {
                    colors.AddRange(child.GetUsedColors());
                }
            }

            return colors;
        }

        public void ChangeHue(double hue, double saturation, double lightness, List<IUndoRedo> undos, bool hueSet = false, int variance = 0)
        {
            if(NodeType == ParticleNodeType.Emission)
            {
                EmissionNode.Texture.Color1.ChangeHue(hue, saturation, lightness, undos, hueSet, variance);
                EmissionNode.Texture.Color2.ChangeHue(hue, saturation, lightness, undos, hueSet, variance);
                EmissionNode.Texture.Color_Variance.ChangeHue(hue, saturation, lightness, undos, hueSet, variance);
            }

            //Children
            if (ChildParticleNodes != null)
            {
                foreach (var child in ChildParticleNodes)
                {
                    child.ChangeHue(hue, saturation, lightness, undos, hueSet);
                }
            }
        }

        public void RemoveColorRandomRange(List<IUndoRedo> undos)
        {
            if(EmissionNode?.Texture != null)
            {
                if (EmissionNode.Texture.Color_Variance != 0f)
                {
                    undos.Add(new UndoableProperty<CustomColor>(nameof(EmissionNode.Texture.Color_Variance.R), EmissionNode.Texture.Color_Variance, EmissionNode.Texture.Color_Variance.R, 0f));
                    undos.Add(new UndoableProperty<CustomColor>(nameof(EmissionNode.Texture.Color_Variance.G), EmissionNode.Texture.Color_Variance, EmissionNode.Texture.Color_Variance.G, 0f));
                    undos.Add(new UndoableProperty<CustomColor>(nameof(EmissionNode.Texture.Color_Variance.B), EmissionNode.Texture.Color_Variance, EmissionNode.Texture.Color_Variance.B, 0f));

                    EmissionNode.Texture.Color_Variance.R = 0f;
                    EmissionNode.Texture.Color_Variance.G = 0f;
                    EmissionNode.Texture.Color_Variance.B = 0f;
                }
            }
        }
    
        public EMP_KeyframedValue[] GetKeyframedValues(int parameter, params int[] components)
        {
            EMP_KeyframedValue[] values = new EMP_KeyframedValue[components.Length];

            for(int i = 0; i < components.Length; i++)
            {
                EMP_KeyframedValue value = KeyframedValues.FirstOrDefault(x => x.Value == parameter && x.Component == components[i]);

                if (value != null)
                    values[i] = value;
                else
                    values[i] = EMP_KeyframedValue.Default;
            }

            return values;
        }

        internal void CompileAllKeyframes()
        {
            KeyframedValues.Clear();

            //Position and Rotation (XYZ) exist on all nodes, regardless of the type
            AddKeyframedValues(Position.CompileKeyframes());
            AddKeyframedValues(Rotation.CompileKeyframes());

            //Emission types always have Scale, Color1 and Color2 values (since they all have a ParticleTexture)
            if(NodeType == ParticleNodeType.Emission)
            {
                AddKeyframedValues(EmissionNode.Texture.Color1.CompileKeyframes());
                AddKeyframedValues(EmissionNode.Texture.Color2.CompileKeyframes());
                AddKeyframedValues(EmissionNode.Texture.Color1_Transparency.CompileKeyframes());
                AddKeyframedValues(EmissionNode.Texture.Color2_Transparency.CompileKeyframes());

                if (UseScaleXY)
                {
                    AddKeyframedValues(EmissionNode.Texture.ScaleXY.CompileKeyframes(true));
                    AddKeyframedValues(EmissionNode.Texture.ScaleBase.CompileKeyframes(true));
                }
                else
                {
                    AddKeyframedValues(EmissionNode.Texture.ScaleBase.CompileKeyframes(false));
                }
            }

            //Node-specific values
            switch (NodeSpecificType)
            {
                case NodeSpecificType.SphericalDistribution:
                    AddKeyframedValues(EmitterNode.Size.CompileKeyframes(isSphere: true));
                    AddKeyframedValues(EmitterNode.Velocity.CompileKeyframes());
                    break;
                case NodeSpecificType.VerticalDistribution:
                    AddKeyframedValues(EmitterNode.Position.CompileKeyframes());
                    AddKeyframedValues(EmitterNode.Velocity.CompileKeyframes());
                    AddKeyframedValues(EmitterNode.Angle.CompileKeyframes());
                    break;
                case NodeSpecificType.ShapeAreaDistribution:
                case NodeSpecificType.ShapePerimeterDistribution:
                    AddKeyframedValues(EmitterNode.Position.CompileKeyframes());
                    AddKeyframedValues(EmitterNode.Velocity.CompileKeyframes());
                    AddKeyframedValues(EmitterNode.Angle.CompileKeyframes());
                    AddKeyframedValues(EmitterNode.Size.CompileKeyframes());
                    AddKeyframedValues(EmitterNode.Size2.CompileKeyframes());
                    break;
                case NodeSpecificType.AutoOriented:
                case NodeSpecificType.Default:
                case NodeSpecificType.Mesh:
                case NodeSpecificType.ShapeDraw:
                    AddKeyframedValues(EmissionNode.ActiveRotation.CompileKeyframes());
                    break;
            }
        }

        internal void AddKeyframedValues(EMP_KeyframedValue[] values)
        {
            for(int i = 0; i < values.Length; i++)
            {
                if(values[i] != null)
                {
                    if(KeyframedValues.Any(x => x.Value == values[i].Value && x.Component == values[i].Component))
                    {
                        throw new Exception($"EMP_File: KeyframedValue already exists (parameter = {values[i].Value}, component = {values[i].Component})");
                    }

                    KeyframedValues.Add(values[i]);
                }
            }
        }
    }

    [Serializable]
    public class ParticleTexture : INotifyPropertyChanged
    {
        #region NotifyPropertyChanged
        [field: NonSerialized]
        public event PropertyChangedEventHandler PropertyChanged;

        // This method is called by the Set accessor of each property.
        // The CallerMemberName attribute that is applied to the optional propertyName
        // parameter causes the property name of the caller to be substituted as an argument.
        private void NotifyPropertyChanged(String propertyName = "")
        {
            if (PropertyChanged != null)
            {
                PropertyChanged(this, new PropertyChangedEventArgs(propertyName));
            }
        }
        #endregion

        public const int ENTRY_SIZE = 112;

        #region References
        private EmmMaterial _materialRef = null;
        public EmmMaterial MaterialRef
        {
            get
            {
                return _materialRef;
            }
            set
            {
                if (_materialRef != value)
                    _materialRef = value;
                NotifyPropertyChanged(nameof(MaterialRef));
            }
        }
        public AsyncObservableCollection<TextureEntry_Ref> TextureEntryRef { get; set; } = new AsyncObservableCollection<TextureEntry_Ref>();

        #endregion

        public byte I_00 { get; set; }
        public byte I_01 { get; set; }
        public byte I_02 { get; set; }
        public byte I_03 { get; set; }

        public float RenderDepth { get; set; }
        public int I_08 { get; set; }
        public int I_12 { get; set; }
        public ushort MaterialID { get; set; }

        //Colors
        public KeyframedColorValue Color1 { get; set; } = new KeyframedColorValue(1, 1, 1, KeyframedValueType.Color1);
        public KeyframedColorValue Color2 { get; set; } = new KeyframedColorValue(1, 1, 1, KeyframedValueType.Color2);
        public CustomColor Color_Variance { get; set; } = new CustomColor();
        public KeyframedFloatValue Color1_Transparency { get; set; } = new KeyframedFloatValue(1f, KeyframedValueType.Color1_Transparency);
        public KeyframedFloatValue Color2_Transparency { get; set; } = new KeyframedFloatValue(1f, KeyframedValueType.Color2_Transparency);

        public KeyframedFloatValue ScaleBase { get; set; } = new KeyframedFloatValue(1f, KeyframedValueType.ScaleBase, true);
        public float ScaleBase_Variance { get; set; }
        public KeyframedVector2Value ScaleXY { get; set; } = new KeyframedVector2Value(1f, 1f, KeyframedValueType.ScaleXY, true);
        public CustomVector4 ScaleXY_Variance { get; set; } = new CustomVector4();
        public float F_96 { get; set; }
        public float F_100 { get; set; }
        public float F_104 { get; set; }
        public float F_108 { get; set; }

        internal static ParticleTexture Parse(byte[] rawBytes, int offset, Parser parser)
        {
            int particleNodeOffset = offset - ParticleNode.ENTRY_SIZE;
            ParticleTexture newTexture = new ParticleTexture();

            newTexture.I_00 = rawBytes[offset + 0];
            newTexture.I_01 = rawBytes[offset + 1];
            newTexture.I_02 = rawBytes[offset + 2];
            newTexture.I_03 = rawBytes[offset + 3];
            newTexture.I_08 = BitConverter.ToInt32(rawBytes, offset + 8);
            newTexture.I_12 = BitConverter.ToInt32(rawBytes, offset + 12);
            newTexture.MaterialID = BitConverter.ToUInt16(rawBytes, offset + 16);
            newTexture.RenderDepth = BitConverter.ToSingle(rawBytes, offset + 4);

            //Scales (scale by 2 when reading, divide by 2 when writing)
            newTexture.ScaleBase.Constant = BitConverter.ToSingle(rawBytes, offset + 24) * 2f;
            newTexture.ScaleBase_Variance = BitConverter.ToSingle(rawBytes, offset + 28) * 2f;
            newTexture.ScaleXY.Constant.X = BitConverter.ToSingle(rawBytes, offset + 32) * 2f;
            newTexture.ScaleXY.Constant.Y = BitConverter.ToSingle(rawBytes, offset + 40) * 2f;
            newTexture.ScaleXY_Variance.X = BitConverter.ToSingle(rawBytes, offset + 36) * 2f;
            newTexture.ScaleXY_Variance.Y = BitConverter.ToSingle(rawBytes, offset + 44) * 2f;

            //Colors
            newTexture.Color1.Constant = new CustomColor(BitConverter.ToSingle(rawBytes, offset + 48), BitConverter.ToSingle(rawBytes, offset + 52), BitConverter.ToSingle(rawBytes, offset + 56), 1f);
            newTexture.Color_Variance = new CustomColor(BitConverter.ToSingle(rawBytes, offset + 64), BitConverter.ToSingle(rawBytes, offset + 68), BitConverter.ToSingle(rawBytes, offset + 72), BitConverter.ToSingle(rawBytes, offset + 76));
            newTexture.Color2.Constant = new CustomColor(BitConverter.ToSingle(rawBytes, offset + 80), BitConverter.ToSingle(rawBytes, offset + 84), BitConverter.ToSingle(rawBytes, offset + 88), 1f);
            newTexture.Color1_Transparency.Constant = BitConverter.ToSingle(rawBytes, offset + 60);
            newTexture.Color2_Transparency.Constant = BitConverter.ToSingle(rawBytes, offset + 92);

            newTexture.F_96 = BitConverter.ToSingle(rawBytes, offset + 96);
            newTexture.F_100 = BitConverter.ToSingle(rawBytes, offset + 100);
            newTexture.F_104 = BitConverter.ToSingle(rawBytes, offset + 104);
            newTexture.F_108 = BitConverter.ToSingle(rawBytes, offset + 108);

            int textureSamplerCount = BitConverter.ToInt16(rawBytes, offset + 18);
            int texturePointerList = BitConverter.ToInt32(rawBytes, offset + 20) + particleNodeOffset;

            if (texturePointerList != particleNodeOffset)
            {
                int textureEntrySize = parser.EmpFile.Version == VersionEnum.SDBH ? EMP_TextureSamplerDef.ENTRY_SIZE_NEW : EMP_TextureSamplerDef.ENTRY_SIZE;

                for (int e = 0; e < textureSamplerCount; e++)
                {
                    int textureOffset = BitConverter.ToInt32(rawBytes, texturePointerList + (4 * e));

                    if(textureOffset != 0)
                    {
                        int texIdx = (textureOffset + particleNodeOffset - parser.TextureSamplersOffset) / textureEntrySize;
                        newTexture.TextureEntryRef.Add(new TextureEntry_Ref(parser.EmpFile.Textures[texIdx]));
                    }
                    else
                    {
                        newTexture.TextureEntryRef.Add(new TextureEntry_Ref(null));
                    }
                }
            }

            return newTexture;
        }

        internal byte[] Write()
        {
            List<byte> bytes = new List<byte>(ENTRY_SIZE);

            bytes.AddRange(new byte[4] { I_00, I_01, I_02, I_03 });
            bytes.AddRange(BitConverter.GetBytes(RenderDepth));
            bytes.AddRange(BitConverter.GetBytes(I_08));
            bytes.AddRange(BitConverter.GetBytes(I_12));
            bytes.AddRange(BitConverter.GetBytes(MaterialID));
            bytes.AddRange(BitConverter.GetBytes((ushort)TextureEntryRef.Count));
            bytes.AddRange(BitConverter.GetBytes((int)0)); //Offset, fill with 0 for now

            bytes.AddRange(BitConverter.GetBytes(ScaleBase.Constant / 2));
            bytes.AddRange(BitConverter.GetBytes(ScaleBase_Variance / 2));
            bytes.AddRange(BitConverter.GetBytes(ScaleXY.Constant.X / 2));
            bytes.AddRange(BitConverter.GetBytes(ScaleXY_Variance.X / 2));
            bytes.AddRange(BitConverter.GetBytes(ScaleXY.Constant.Y / 2));
            bytes.AddRange(BitConverter.GetBytes(ScaleXY_Variance.Y / 2));
            bytes.AddRange(BitConverter.GetBytes(Color1.Constant.R));
            bytes.AddRange(BitConverter.GetBytes(Color1.Constant.G));
            bytes.AddRange(BitConverter.GetBytes(Color1.Constant.B));
            bytes.AddRange(BitConverter.GetBytes(Color1_Transparency.Constant));
            bytes.AddRange(BitConverter.GetBytes(Color_Variance.R));
            bytes.AddRange(BitConverter.GetBytes(Color_Variance.G));
            bytes.AddRange(BitConverter.GetBytes(Color_Variance.B));
            bytes.AddRange(BitConverter.GetBytes(Color_Variance.A));
            bytes.AddRange(BitConverter.GetBytes(Color2.Constant.R));
            bytes.AddRange(BitConverter.GetBytes(Color2.Constant.G));
            bytes.AddRange(BitConverter.GetBytes(Color2.Constant.B));
            bytes.AddRange(BitConverter.GetBytes(Color2_Transparency.Constant));
            bytes.AddRange(BitConverter.GetBytes(F_96));
            bytes.AddRange(BitConverter.GetBytes(F_100));
            bytes.AddRange(BitConverter.GetBytes(F_104));
            bytes.AddRange(BitConverter.GetBytes(F_108));

            return bytes.ToArray();
        }
       
        public ParticleTexture Clone()
        {
            AsyncObservableCollection<TextureEntry_Ref> textureRefs = new AsyncObservableCollection<TextureEntry_Ref>();

            if(TextureEntryRef != null)
            {
                foreach(var textRef in TextureEntryRef)
                {
                    textureRefs.Add(new TextureEntry_Ref() { TextureRef = textRef.TextureRef });
                }
            }

            return new ParticleTexture()
            {
                I_00 = I_00,
                I_01 = I_01,
                I_02 = I_02,
                I_03 = I_03,
                I_08 = I_08,
                I_12 = I_12,
                MaterialID = MaterialID,
                RenderDepth = RenderDepth,
                F_100 = F_100,
                F_104 = F_104,
                F_108 = F_108,
                ScaleBase = ScaleBase,
                ScaleBase_Variance = ScaleBase_Variance,
                ScaleXY = ScaleXY,
                Color1 = Color1.Copy(),
                Color_Variance = Color_Variance.Copy(),
                Color2 = Color2.Copy(),
                Color1_Transparency = Color1_Transparency.Copy(),
                Color2_Transparency = Color2_Transparency.Copy(),
                F_96 = F_96,
                MaterialRef = MaterialRef,
                TextureEntryRef = textureRefs
            };
        }

        public static ParticleTexture GetNew()
        {
            return new ParticleTexture()
            {
                TextureEntryRef = new AsyncObservableCollection<TextureEntry_Ref>()
                {
                    //These are created because EMP Editor relies on there always being 2 texture references. Technically there can be any number of these, but only 2 are ever used by the game EMP files, so thats all that the EMP Editor exposes.
                    new TextureEntry_Ref(), new TextureEntry_Ref()
                }
            };
        }
    }

    [Serializable]
    public class ParticleEmitter
    {
        public enum ParticleEmitterShape
        {
            Circle = 0, //"ShapePerimeterDistribution" or "ShapeAreaDistribution"
            Square = 1, //"ShapePerimeterDistribution" or "ShapeAreaDistribution"
            Sphere, //"SphericalDistribution"
            Cone //"VerticalDistribution"
        }

        public ParticleEmitterShape Shape { get; set; }

        //Differentiates emitter type 2 and 3 ("ShapePerimeterDistribution" and "ShapeAreaDistribution")
        public bool EmitFromArea { get; set; }

        //Position along Up vector. Used by all except "Sphere"
        public KeyframedFloatValue Position { get; set; } = new KeyframedFloatValue(0f, KeyframedValueType.PositionY);
        public float Position_Variance { get; set; }

        //Velocity along up vector. Used for all shapes.
        public KeyframedFloatValue Velocity { get; set; } = new KeyframedFloatValue(0f, KeyframedValueType.Velocity);
        public float Velocity_Variance { get; set; }

        //Used for all except "Cone"
        public KeyframedFloatValue Size { get; set; } = new KeyframedFloatValue(1f, KeyframedValueType.Size1);
        public float Size_Variance { get; set; }

        //Used for "Square"
        public KeyframedFloatValue Size2 { get; set; } = new KeyframedFloatValue(1f, KeyframedValueType.Size2);
        public float Size2_Variance { get; set; }

        //Angles the shape. Used by all except "Sphere"
        public KeyframedFloatValue Angle { get; set; } = new KeyframedFloatValue(0f, KeyframedValueType.Angle);
        public float Angle_Variance { get; set; }

        //Two unknowns on "Cone", and one on "Cirlce" and "Square"
        public float F_1 { get; set; }
        public float F_2 { get; set; }

        internal static ParticleEmitter Parse(byte[] bytes, int offset)
        {
            ParticleEmitter emitter = new ParticleEmitter();
            byte emitterType = bytes[offset + 36];

            offset += ParticleNode.ENTRY_SIZE;

            if(emitterType == 0)
            {
                //Cone
                emitter.Shape = ParticleEmitterShape.Cone;
                emitter.Position.Constant = BitConverter.ToSingle(bytes, offset + 0);
                emitter.Position_Variance = BitConverter.ToSingle(bytes, offset + 4);
                emitter.Velocity.Constant = BitConverter.ToSingle(bytes, offset + 8);
                emitter.Velocity_Variance = BitConverter.ToSingle(bytes, offset + 12);
                emitter.Angle.Constant = BitConverter.ToSingle(bytes, offset + 16);
                emitter.Angle_Variance = BitConverter.ToSingle(bytes, offset + 20);
                emitter.F_1 = BitConverter.ToSingle(bytes, offset + 24);
                emitter.F_2 = BitConverter.ToSingle(bytes, offset + 28);
            }
            else if(emitterType == 1)
            {
                //Sphere
                emitter.Shape = ParticleEmitterShape.Sphere;
                emitter.Size.Constant = BitConverter.ToSingle(bytes, offset + 0);
                emitter.Size_Variance = BitConverter.ToSingle(bytes, offset + 4);
                emitter.Velocity.Constant = BitConverter.ToSingle(bytes, offset + 8);
                emitter.Velocity_Variance = BitConverter.ToSingle(bytes, offset + 12);
            }
            else if(emitterType == 2 || emitterType == 3)
            {
                //Cirlce and Square
                int shape = BitConverter.ToInt32(bytes, offset + 40);

                if (shape != 0 && shape != 1)
                    throw new ArgumentException($"ParticleEmitter.Parse: Invalid Shape parameter on Square/Circle type (Read {shape}, expected 0 or 1).");

                emitter.Shape = (ParticleEmitterShape)shape;
                emitter.EmitFromArea = emitterType == 2;
                emitter.Position.Constant = BitConverter.ToSingle(bytes, offset + 0);
                emitter.Position_Variance = BitConverter.ToSingle(bytes, offset + 4);
                emitter.Velocity.Constant = BitConverter.ToSingle(bytes, offset + 8);
                emitter.Velocity_Variance = BitConverter.ToSingle(bytes, offset + 12);
                emitter.Angle.Constant = BitConverter.ToSingle(bytes, offset + 16);
                emitter.Angle_Variance = BitConverter.ToSingle(bytes, offset + 20);
                emitter.Size.Constant = BitConverter.ToSingle(bytes, offset + 24);
                emitter.Size_Variance = BitConverter.ToSingle(bytes, offset + 28);
                emitter.Size2.Constant = BitConverter.ToSingle(bytes, offset + 32);
                emitter.Size2_Variance = BitConverter.ToSingle(bytes, offset + 36);
                emitter.F_1 = BitConverter.ToSingle(bytes, offset + 44);
            }

            return emitter;
        }

        internal byte[] Write(ref byte emitterType)
        {
            List<byte> bytes = new List<byte>();

            if(Shape == ParticleEmitterShape.Cone)
            {
                emitterType = 0;
                bytes.AddRange(BitConverter.GetBytes(Position.Constant));
                bytes.AddRange(BitConverter.GetBytes(Position_Variance));
                bytes.AddRange(BitConverter.GetBytes(Velocity.Constant));
                bytes.AddRange(BitConverter.GetBytes(Velocity_Variance));
                bytes.AddRange(BitConverter.GetBytes(Angle.Constant));
                bytes.AddRange(BitConverter.GetBytes(Angle_Variance));
                bytes.AddRange(BitConverter.GetBytes(F_1));
                bytes.AddRange(BitConverter.GetBytes(F_2));
            }
            else if(Shape == ParticleEmitterShape.Sphere)
            {
                emitterType = 1;
                bytes.AddRange(BitConverter.GetBytes(Size.Constant));
                bytes.AddRange(BitConverter.GetBytes(Size_Variance));
                bytes.AddRange(BitConverter.GetBytes(Velocity.Constant));
                bytes.AddRange(BitConverter.GetBytes(Velocity_Variance));
            }
            else if(Shape == ParticleEmitterShape.Circle || Shape == ParticleEmitterShape.Square)
            {
                emitterType = (byte)(EmitFromArea ? 2 : 3);
                bytes.AddRange(BitConverter.GetBytes(Position.Constant));
                bytes.AddRange(BitConverter.GetBytes(Position_Variance));
                bytes.AddRange(BitConverter.GetBytes(Velocity.Constant));
                bytes.AddRange(BitConverter.GetBytes(Velocity_Variance));
                bytes.AddRange(BitConverter.GetBytes(Angle.Constant));
                bytes.AddRange(BitConverter.GetBytes(Angle_Variance));
                bytes.AddRange(BitConverter.GetBytes(Size.Constant));
                bytes.AddRange(BitConverter.GetBytes(Size_Variance));
                bytes.AddRange(BitConverter.GetBytes(Size2.Constant));
                bytes.AddRange(BitConverter.GetBytes(Size2_Variance));
                bytes.AddRange(BitConverter.GetBytes((int)Shape));
                bytes.AddRange(BitConverter.GetBytes(F_1));
            }

            return bytes.ToArray();
        }

        internal NodeSpecificType GetNodeType()
        {
            switch (Shape)
            {
                case ParticleEmitterShape.Sphere:
                    return NodeSpecificType.SphericalDistribution;
                case ParticleEmitterShape.Cone:
                    return NodeSpecificType.VerticalDistribution;
                case ParticleEmitterShape.Circle:
                case ParticleEmitterShape.Square:
                    return EmitFromArea ? NodeSpecificType.ShapePerimeterDistribution : NodeSpecificType.ShapeAreaDistribution;
            }

            return NodeSpecificType.Null;
        }
    }

    [Serializable]
    public class ParticleEmission
    {
        public enum ParticleEmissionType
        {
            Default,
            ConeExtrude,
            ShapeDraw,
            Mesh
        }

        public ParticleEmissionType EmissionType { get; set; }
        public ParticleTexture Texture { get; set; } = ParticleTexture.GetNew();

        //Default:
        public bool AutoRotation { get; set; }
        public bool VisibleOnlyOnMotion { get; set; } //Requires AutoRotation

        //Particle starts at this angle
        public float StartRotation { get; set; }
        public float StartRotation_Variance { get; set; }

        //Active rotation (degrees/second)
        public KeyframedFloatValue ActiveRotation { get; set; } = new KeyframedFloatValue(0f, KeyframedValueType.ActiveRotation);
        public float ActiveRotation_Variance { get; set; }

        //Defines a RotationAxis to be used. Only used in Mesh or when AutoRotation is disabled
        public CustomVector4 RotationAxis { get; set; } = new CustomVector4();

        //Specialised types:
        public ConeExtrude ConeExtrude { get; set; } = new ConeExtrude();
        public ShapeDraw ShapeDraw { get; set; } = new ShapeDraw();
        public ParticleEmgMesh Mesh { get; set; } = new ParticleEmgMesh();

        internal static ParticleEmission Parse(byte[] bytes, int offset, Parser parser)
        {
            ParticleEmission emission = new ParticleEmission();
            byte emissionType = bytes[offset + 36];
            int nodeOffset = offset;

            //Load texture
            offset += ParticleNode.ENTRY_SIZE;
            emission.Texture = ParticleTexture.Parse(bytes, offset, parser);

            //Load emission type
            offset += ParticleTexture.ENTRY_SIZE;

            if(emissionType == 0)
            {
                //AutoOriented
                emission.EmissionType = ParticleEmissionType.Default;
                emission.AutoRotation = true;
                emission.VisibleOnlyOnMotion = false;
                emission.StartRotation = BitConverter.ToSingle(bytes, offset + 0);
                emission.StartRotation_Variance = BitConverter.ToSingle(bytes, offset + 4);
                emission.ActiveRotation.Constant = BitConverter.ToSingle(bytes, offset + 8);
                emission.ActiveRotation_Variance = BitConverter.ToSingle(bytes, offset + 12);
            }
            else if(emissionType == 1)
            {
                emission.EmissionType = ParticleEmissionType.Default;
                emission.AutoRotation = true;
                emission.VisibleOnlyOnMotion = true;
            }
            else if(emissionType == 2)
            {
                emission.EmissionType = ParticleEmissionType.Default;
                emission.AutoRotation = false;
                emission.VisibleOnlyOnMotion = false;
                emission.StartRotation = BitConverter.ToSingle(bytes, offset + 0);
                emission.StartRotation_Variance = BitConverter.ToSingle(bytes, offset + 4);
                emission.ActiveRotation.Constant = BitConverter.ToSingle(bytes, offset + 8);
                emission.ActiveRotation_Variance = BitConverter.ToSingle(bytes, offset + 12);
                emission.RotationAxis.X = BitConverter.ToSingle(bytes, offset + 16);
                emission.RotationAxis.Y = BitConverter.ToSingle(bytes, offset + 20);
                emission.RotationAxis.Z = BitConverter.ToSingle(bytes, offset + 24);
            }
            else if(emissionType == 3)
            {
                emission.EmissionType = ParticleEmissionType.ConeExtrude;

                emission.ConeExtrude.Duration = BitConverter.ToUInt16(bytes, offset + 0);
                emission.ConeExtrude.Duration_Variance = BitConverter.ToUInt16(bytes, offset + 2);
                emission.ConeExtrude.TimeBetweenTwoStep = BitConverter.ToUInt16(bytes, offset + 4);
                emission.ConeExtrude.I_08 = BitConverter.ToUInt16(bytes, offset + 8);
                emission.ConeExtrude.I_10 = BitConverter.ToUInt16(bytes, offset + 10);

                int count = BitConverter.ToInt16(bytes, offset + 6) + 1;
                int listOffset = BitConverter.ToInt32(bytes, offset + 12) + nodeOffset;

                for (int i = 0; i < count; i++)
                {
                    emission.ConeExtrude.Points.Add(new ConeExtrudePoints()
                    {
                        WorldScaleFactor = BitConverter.ToSingle(bytes, listOffset + 0),
                        WorldScaleAdd = BitConverter.ToSingle(bytes, listOffset + 4),
                        WorldOffsetFactor = BitConverter.ToSingle(bytes, listOffset + 8),
                        WorldOffsetFactor2 = BitConverter.ToSingle(bytes, listOffset + 12)
                    });

                    listOffset += 16;
                }
            }
            else if(emissionType == 4)
            {
                emission.EmissionType = ParticleEmissionType.Mesh;

                emission.StartRotation = BitConverter.ToSingle(bytes, offset + 0);
                emission.StartRotation_Variance = BitConverter.ToSingle(bytes, offset + 4);
                emission.ActiveRotation.Constant = BitConverter.ToSingle(bytes, offset + 8);
                emission.ActiveRotation_Variance = BitConverter.ToSingle(bytes, offset + 12);

                emission.RotationAxis.X = BitConverter.ToSingle(bytes, offset + 16);
                emission.RotationAxis.Y = BitConverter.ToSingle(bytes, offset + 20);
                emission.RotationAxis.Z = BitConverter.ToSingle(bytes, offset + 24);
                emission.Mesh.I_32 = BitConverter.ToInt32(bytes, offset + 32);
                emission.Mesh.I_40 = BitConverter.ToInt32(bytes, offset + 40);
                emission.Mesh.I_44 = BitConverter.ToInt32(bytes, offset + 44);

                int emgOffset = BitConverter.ToInt32(bytes, offset + 36) + nodeOffset;
                //emission.Mesh.EmgFile = EMG.EMG_File.Read(bytes, emgOffset);

                //For testing purposes, keep using the old EMG code path for now. Parsing and saving the EMG file will produce a different binary result, which isn't good for comparisons
                int emgSize = parser.CalculateEmgSize(emgOffset - nodeOffset, nodeOffset);
                emission.Mesh.EmgBytes = bytes.GetRange(emgOffset, emgSize);

            }
            else if (emissionType == 5)
            {
                emission.EmissionType = ParticleEmissionType.ShapeDraw;
                emission.StartRotation = BitConverter.ToSingle(bytes, offset + 0);
                emission.StartRotation_Variance = BitConverter.ToSingle(bytes, offset + 4);
                emission.ActiveRotation.Constant = BitConverter.ToSingle(bytes, offset + 8);
                emission.ActiveRotation_Variance = BitConverter.ToSingle(bytes, offset + 12);

                emission.ShapeDraw.AutoRotationType = (ParticleAutoRotationType)BitConverter.ToUInt16(bytes, offset + 18);
                emission.ShapeDraw.I_24 = BitConverter.ToUInt16(bytes, offset + 24);
                emission.ShapeDraw.I_26 = BitConverter.ToUInt16(bytes, offset + 26);
                emission.ShapeDraw.I_28 = BitConverter.ToUInt16(bytes, offset + 28);
                emission.ShapeDraw.I_30 = BitConverter.ToUInt16(bytes, offset + 30);

                int pointCount = BitConverter.ToInt16(bytes, offset + 16) + 1;
                int pointsOffset = BitConverter.ToInt32(bytes, offset + 20) + nodeOffset;

                for (int i = 0; i < pointCount; i++)
                {
                    emission.ShapeDraw.Points.Add(new ShapeDrawPoints()
                    {
                        X = BitConverter.ToSingle(bytes, pointsOffset + 0),
                        Y = BitConverter.ToSingle(bytes, pointsOffset + 4)
                    });

                    pointsOffset += 8;
                }
            }

            return emission;
        }

        internal byte[] Write(ref byte emissionType, int nodeStart, Deserializer writer)
        {
            int textureStart = nodeStart + ParticleNode.ENTRY_SIZE;
            int currentRelativeOffset = ParticleNode.ENTRY_SIZE;;
            List<byte> bytes = new List<byte>();

            //Write TexturePart
            bytes.AddRange(Texture.Write());
            currentRelativeOffset += ParticleTexture.ENTRY_SIZE;

            if(bytes.Count != ParticleTexture.ENTRY_SIZE)
            {
                throw new Exception("TexturePart invalid size!");
            }

            //Write emission data
            switch (EmissionType)
            {
                case ParticleEmissionType.Default:
                    if(AutoRotation && VisibleOnlyOnMotion)
                    {
                        //"VisibleOnSpeed"
                        emissionType = 1;
                    }
                    else if (AutoRotation)
                    {
                        //"AutoOriented"
                        emissionType = 0;
                        bytes.AddRange(BitConverter.GetBytes(StartRotation));
                        bytes.AddRange(BitConverter.GetBytes(StartRotation_Variance));
                        bytes.AddRange(BitConverter.GetBytes(ActiveRotation.Constant));
                        bytes.AddRange(BitConverter.GetBytes(ActiveRotation_Variance));
                        currentRelativeOffset += 16;
                    }
                    else
                    {
                        //"Default"
                        emissionType = 2;
                        bytes.AddRange(BitConverter.GetBytes(StartRotation));
                        bytes.AddRange(BitConverter.GetBytes(StartRotation_Variance));
                        bytes.AddRange(BitConverter.GetBytes(ActiveRotation.Constant));
                        bytes.AddRange(BitConverter.GetBytes(ActiveRotation_Variance));
                        bytes.AddRange(BitConverter.GetBytes(RotationAxis.X));
                        bytes.AddRange(BitConverter.GetBytes(RotationAxis.Y));
                        bytes.AddRange(BitConverter.GetBytes(RotationAxis.Z));
                        bytes.AddRange(BitConverter.GetBytes(0f)); //W is never used
                        currentRelativeOffset += 32;
                    }

                    break;
                case ParticleEmissionType.ConeExtrude:
                    emissionType = 3;

                    byte[] coneStruct = ConeExtrude.Write(currentRelativeOffset);
                    bytes.AddRange(coneStruct);

                    currentRelativeOffset += coneStruct.Length;
                    break;
                case ParticleEmissionType.Mesh:
                    emissionType = 4;

                    bytes.AddRange(BitConverter.GetBytes(StartRotation));
                    bytes.AddRange(BitConverter.GetBytes(StartRotation_Variance));
                    bytes.AddRange(BitConverter.GetBytes(ActiveRotation.Constant));
                    bytes.AddRange(BitConverter.GetBytes(ActiveRotation_Variance));
                    bytes.AddRange(BitConverter.GetBytes(RotationAxis.X));
                    bytes.AddRange(BitConverter.GetBytes(RotationAxis.Y));
                    bytes.AddRange(BitConverter.GetBytes(RotationAxis.Z));
                    bytes.AddRange(BitConverter.GetBytes(0f));
                    bytes.AddRange(BitConverter.GetBytes(Mesh.I_32));
                    bytes.AddRange(new byte[4]);
                    bytes.AddRange(BitConverter.GetBytes(Mesh.I_40));
                    bytes.AddRange(BitConverter.GetBytes(Mesh.I_44));

                    currentRelativeOffset += 48;
                    break;
                case ParticleEmissionType.ShapeDraw:
                    emissionType = 5;

                    bytes.AddRange(BitConverter.GetBytes(StartRotation));
                    bytes.AddRange(BitConverter.GetBytes(StartRotation_Variance));
                    bytes.AddRange(BitConverter.GetBytes(ActiveRotation.Constant));
                    bytes.AddRange(BitConverter.GetBytes(ActiveRotation_Variance));

                    int indexCount = ShapeDraw.Points.Count - 1;

                    bytes.AddRange(BitConverter.GetBytes((ushort)indexCount));
                    bytes.AddRange(BitConverter.GetBytes((ushort)ShapeDraw.AutoRotationType));
                    bytes.AddRange(BitConverter.GetBytes(currentRelativeOffset + 32));
                    bytes.AddRange(BitConverter.GetBytes(ShapeDraw.I_24));
                    bytes.AddRange(BitConverter.GetBytes(ShapeDraw.I_26));
                    bytes.AddRange(BitConverter.GetBytes(ShapeDraw.I_28));
                    bytes.AddRange(BitConverter.GetBytes(ShapeDraw.I_30));
                    currentRelativeOffset += 32;

                    foreach (ShapeDrawPoints point in ShapeDraw.Points)
                    {
                        bytes.AddRange(BitConverter.GetBytes(point.X));
                        bytes.AddRange(BitConverter.GetBytes(point.Y));
                        currentRelativeOffset += 4;
                    }
                    break;
            }

            //Write texture offsets
            Utils.ReplaceRange(bytes, BitConverter.GetBytes(bytes.Count + ParticleNode.ENTRY_SIZE), 20);

            foreach (TextureEntry_Ref samplers in Texture.TextureEntryRef)
            {
                if (samplers.TextureRef != null)
                {
                    int textureIdx = writer.EmpFile.Textures.IndexOf(samplers.TextureRef);
                    
                    //Just a safe-guard - this idealy shouldn't happen
                    if(textureIdx == -1)
                    {
                        textureIdx = writer.EmpFile.Textures.Count;
                        writer.EmpFile.Textures.Add(samplers.TextureRef);

                        writer.EmbTextureOffsets.Add(new List<int>());
                        writer.EmbTextureOffsets_Minus.Add(new List<int>());
                    }

                    writer.EmbTextureOffsets[textureIdx].Add(nodeStart + currentRelativeOffset);
                    writer.EmbTextureOffsets_Minus[textureIdx].Add(nodeStart);
                }

                bytes.AddRange(new byte[4]);
                currentRelativeOffset += 4;
            }

            //Write mesh data
            if(EmissionType == ParticleEmissionType.Mesh)
            {
                byte[] emgBytes;

                if(Mesh.EmgFile != null)
                {
                    emgBytes = Mesh.EmgFile.Write();
                }
                else if(Mesh.EmgBytes != null)
                {
                    emgBytes = Mesh.EmgBytes;
                }
                else
                {
                    emgBytes = null;
                }

                if(emgBytes != null)
                {
                    int meshOffset = ParticleTexture.ENTRY_SIZE + 36;
                    Utils.ReplaceRange(bytes, BitConverter.GetBytes(bytes.Count + ParticleNode.ENTRY_SIZE), meshOffset);

                    bytes.AddRange(emgBytes);
                }
            }

            return bytes.ToArray();
        }
    
        public ParticleEmission Clone()
        {
            return new ParticleEmission()
            {
                EmissionType = EmissionType,
                AutoRotation = AutoRotation,
                VisibleOnlyOnMotion = VisibleOnlyOnMotion,
                StartRotation = StartRotation,
                StartRotation_Variance = StartRotation_Variance,
                ActiveRotation = ActiveRotation.Copy(),
                ActiveRotation_Variance = ActiveRotation_Variance,
                RotationAxis = RotationAxis.Copy(),
                ConeExtrude = ConeExtrude.Copy(),
                ShapeDraw = ShapeDraw.Copy(),
                Mesh = Mesh.Copy(),
                Texture = Texture.Clone()
            };
        }
   
        internal NodeSpecificType GetNodeType()
        {
            switch (EmissionType)
            {
                case ParticleEmissionType.Default:
                    if (AutoRotation && VisibleOnlyOnMotion)
                    {
                        return NodeSpecificType.AutoOriented_VisibleOnSpeed;
                    }
                    else if (AutoRotation)
                    {
                        return NodeSpecificType.AutoOriented;
                    }
                    else
                    {
                        return NodeSpecificType.Default;
                    }
                case ParticleEmissionType.ConeExtrude:
                    return NodeSpecificType.ConeExtrude;
                case ParticleEmissionType.Mesh:
                    return NodeSpecificType.Mesh;
                case ParticleEmissionType.ShapeDraw:
                    return NodeSpecificType.ShapeDraw;
                default:
                    return NodeSpecificType.Null;

            }
        }
    }

    [Serializable]
    public class ConeExtrude
    {
        public ushort Duration { get; set; }
        public ushort Duration_Variance { get; set; }
        public ushort TimeBetweenTwoStep { get; set; }
        public ushort I_08 { get; set; }
        public ushort I_10 { get; set; }
        public AsyncObservableCollection<ConeExtrudePoints> Points { get; set; } = new AsyncObservableCollection<ConeExtrudePoints>();

        internal byte[] Write(int currentRelativeOffset)
        {
            List<byte> bytes = new List<byte>();

            int indexCount = Points.Count - 1;

            bytes.AddRange(BitConverter.GetBytes(Duration));
            bytes.AddRange(BitConverter.GetBytes(Duration_Variance));
            bytes.AddRange(BitConverter.GetBytes(TimeBetweenTwoStep));
            bytes.AddRange(BitConverter.GetBytes((ushort)indexCount));
            bytes.AddRange(BitConverter.GetBytes(I_08));
            bytes.AddRange(BitConverter.GetBytes(I_10));
            bytes.AddRange(BitConverter.GetBytes(currentRelativeOffset + 16));

            foreach (ConeExtrudePoints entry in Points)
            {
                bytes.AddRange(BitConverter.GetBytes(entry.WorldScaleFactor));
                bytes.AddRange(BitConverter.GetBytes(entry.WorldScaleAdd));
                bytes.AddRange(BitConverter.GetBytes(entry.WorldOffsetFactor));
                bytes.AddRange(BitConverter.GetBytes(entry.WorldOffsetFactor2));
            }

            //Size = bytes.count
            return bytes.ToArray();
        }
    }

    [Serializable]
    public class ConeExtrudePoints
    {
        public float WorldScaleFactor { get; set; }
        public float WorldScaleAdd { get; set; }
        public float WorldOffsetFactor { get; set; }
        public float WorldOffsetFactor2 { get; set; }

    }

    [Serializable]
    public class ShapeDraw
    {
        public ParticleAutoRotationType AutoRotationType { get; set; } //uint16
        public ushort I_24 { get; set; }
        public ushort I_26 { get; set; }
        public ushort I_28 { get; set; }
        public ushort I_30 { get; set; }

        public AsyncObservableCollection<ShapeDrawPoints> Points { get; set; } = new AsyncObservableCollection<ShapeDrawPoints>();

    }

    [Serializable]
    public class ShapeDrawPoints
    {
        public float X { get; set; }
        public float Y { get; set; }
    }

    [Serializable]
    public class ParticleEmgMesh
    {
        public int I_32 { get; set; }
        public int I_40 { get; set; }
        public int I_44 { get; set; }

        public EMG.EMG_File EmgFile { get; set; }
        public byte[] EmgBytes { get; set; }

    }

    [Serializable]
    public class EMP_KeyframedValue
    {
        public const byte VALUE_POSITION = 0;
        public const byte VALUE_ROTATION = 1;
        public const byte VALUE_SCALE = 2;
        public const byte VALUE_COLOR1 = 3;
        public const byte VALUE_COLOR2 = 4;
        public const byte COMPONENT_R = 0;
        public const byte COMPONENT_G = 1;
        public const byte COMPONENT_B = 2;
        public const byte COMPONENT_A = 3;
        public const byte COMPONENT_X = 0;
        public const byte COMPONENT_Y = 1;
        public const byte COMPONENT_Z = 2;

        /// <summary>
        /// This is the default <see cref="EMP_KeyframedValue"/> instance. Do not use this for anything other than representing a "null" or "empty" keyframed value.
        /// </summary>
        public static EMP_KeyframedValue Default { get; private set; } = new EMP_KeyframedValue() { IsDefault = true };
        /// <summary>
        /// This is the default <see cref="EMP_KeyframedValue"/> instance. Do not use this for anything other than representing a "null" or "empty" keyframed value.
        /// </summary>
        public bool IsDefault { get; private set; }

        public byte Value { get; set; }
        public byte Component { get; set; }
        public bool Interpolate { get; set; }
        public bool Loop { get; set; }
        public byte I_03 { get; set; }
        public float F_04 { get; set; }
        public ushort Duration => (ushort)(Keyframes.Count > 1 ? Keyframes.Max(x => x.Time) + 1 : 0);
        public AsyncObservableCollection<EMP_Keyframe> Keyframes { get; set; } = new AsyncObservableCollection<EMP_Keyframe>();


        public void SetParameters(int parameter, int component)
        {
            Value = (byte)parameter;
            Component = (byte)component;
        }

        /// <summary>
        /// Returns the Parameter, Component and Looped value as an Int16 for writing a binary EMP file.
        /// </summary>
        public short GetParameters()
        {
            byte _I_00 = Value;
            byte _I_01_a = Component;
            byte _I_01 = 0;

            _I_01 = Int4Converter.GetByte(_I_01_a, BitConverter_Ex.GetBytes(Interpolate), "Type0_Animation: Component", "Type0_Animation: Interpolated");

            return BitConverter.ToInt16(new byte[2] { _I_00, _I_01 }, 0);
        }

        public EMP_KeyframedValue Clone()
        {
            return this.Copy();
        }

        public void SetValue(int time, float value, List<IUndoRedo> undos = null)
        {
            foreach (var keyframe in Keyframes)
            {
                if (keyframe.Time == time)
                {
                    float oldValue = keyframe.Value;
                    keyframe.Value = value;

                    if (undos != null)
                        undos.Add(new UndoableProperty<EMP_Keyframe>(nameof(keyframe.Value), keyframe, oldValue, value));

                    return;
                }
            }

            //Keyframe doesn't exist. Add it.
            Keyframes.Add(new EMP_Keyframe() { Time = (ushort)time, Value = value });
        }

        public static byte GetParameter(KeyframedValueType value, bool isSphere = false)
        {
            switch (value)
            {
                case KeyframedValueType.Position:
                    return 0;
                case KeyframedValueType.Rotation:
                case KeyframedValueType.ActiveRotation:
                    return 1;
                case KeyframedValueType.ScaleBase:
                case KeyframedValueType.ScaleXY:
                case KeyframedValueType.PositionY:
                case KeyframedValueType.Velocity:
                case KeyframedValueType.Angle:
                    return 2;
                case KeyframedValueType.Color1:
                case KeyframedValueType.Color1_Transparency:
                case KeyframedValueType.Size1:
                    //Radius for sphere is at a different component.
                    return (byte)(isSphere ? 2 : 3);
                case KeyframedValueType.Size2:
                    return 3;
                case KeyframedValueType.Color2:
                case KeyframedValueType.Color2_Transparency:
                    return 4;
                default:
                    return 0;
            }
        }

        public static byte[] GetComponent(KeyframedValueType value, bool isScaleXyEnabled = false)
        {
            switch (value)
            {
                case KeyframedValueType.Position:
                case KeyframedValueType.Rotation:
                case KeyframedValueType.Color1:
                case KeyframedValueType.Color2:
                    return new byte[] { 0, 1, 2 };
                case KeyframedValueType.ActiveRotation:
                    return new byte[] { 3 };
                case KeyframedValueType.ScaleBase:
                    return isScaleXyEnabled ? new byte[] { 2 } : new byte[] { 0 };
                case KeyframedValueType.ScaleXY:
                    return new byte[] { 0, 1 };
                case KeyframedValueType.PositionY:
                case KeyframedValueType.Color1_Transparency:
                case KeyframedValueType.Color2_Transparency:
                case KeyframedValueType.Size1:
                    return new byte[] { 0 };
                case KeyframedValueType.Velocity:
                case KeyframedValueType.Size2:
                    return new byte[] { 1 };
                case KeyframedValueType.Angle:
                    return new byte[] { 2 };
                default:
                    return new byte[] { 0 };
            }
        }

    }

    [Serializable]
    public class EMP_Keyframe : IKeyframe, ISortable
    {
        public int SortID { get { return Time; } }

        public ushort Time { get; set; }
        public float Value { get; set; }

        public static float GetInterpolatedKeyframe<T>(IList<T> keyframes, int time, bool interpolationEnabled) where T : IKeyframe
        {
            int prev = -1;
            int next = -1;

            foreach (var keyframe in keyframes.OrderBy(x => x.Time))
            {
                if (keyframe.Time > prev && prev < time)
                    prev = keyframe.Time;

                if (keyframe.Time > time)
                {
                    next = keyframe.Time;
                    break;
                }
            }

            //No prev keyframe exists, so no interpolation is possible. Just use next keyframe then
            if (prev == -1)
            {
                return keyframes.FirstOrDefault(x => x.Time == next).Value;
            }

            //Same, but for next keyframe. We will use the prev keyframe here.
            if (next == -1 || prev == next)
            {
                return keyframes.FirstOrDefault(x => x.Time == prev).Value;
            }

            float factor = (time - prev) / (next - prev);
            float prevKeyframe = keyframes.FirstOrDefault(x => x.Time == prev).Value;
            float nextKeyframe = keyframes.FirstOrDefault(x => x.Time == next).Value;

            return interpolationEnabled ? MathHelpers.Lerp(prevKeyframe, nextKeyframe, factor) : prevKeyframe;
        }
    }

    [Serializable]
    public class EMP_KeyframeGroup
    {
        public byte I_00 { get; set; }
        public byte I_01 { get; set; }
        public AsyncObservableCollection<EMP_KeyframedValue> KeyframedValues { get; set; }

        public EMP_KeyframeGroup Clone()
        {
            AsyncObservableCollection<EMP_KeyframedValue> _Entries = AsyncObservableCollection<EMP_KeyframedValue>.Create();
            foreach (var e in KeyframedValues)
            {
                _Entries.Add(e.Clone());
            }

            return new EMP_KeyframeGroup()
            {
                I_00 = I_00,
                I_01 = I_01,
                KeyframedValues = _Entries
            };
        }

        public static EMP_KeyframeGroup GetNew()
        {
            return new EMP_KeyframeGroup()
            {
                KeyframedValues = AsyncObservableCollection<EMP_KeyframedValue>.Create()
            };
        }
    }

    [Serializable]
    public class EMP_TextureSamplerDef : INotifyPropertyChanged
    {
        #region NotifyPropChanged
        [field: NonSerialized]
        public event PropertyChangedEventHandler PropertyChanged;

        public void NotifyPropertyChanged(string propertyName = "")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
        #endregion

        internal const int ENTRY_SIZE = 28;
        internal const int ENTRY_SIZE_NEW = 36;

        private EmbEntry _textureRef = null;
        public EmbEntry TextureRef
        {
            get
            {
                return this._textureRef;
            }
            set
            {
                if (value != this._textureRef)
                {
                    this._textureRef = value;
                    NotifyPropertyChanged(nameof(TextureRef));
                    NotifyPropertyChanged(nameof(ToolName));
                }
            }
        }

        public string ToolName => _textureRef != null ? TextureRef.Name : "No Texture Assigned";

        private TextureAnimationType TextureTypeValue = TextureAnimationType.Static;
        public TextureAnimationType TextureType
        {
            get
            {
                return this.TextureTypeValue;
            }

            set
            {
                if (value != this.TextureTypeValue)
                {
                    this.TextureTypeValue = value;
                    NotifyPropertyChanged("TextureType");
                    NotifyPropertyChanged("IsType0Visible");
                    NotifyPropertyChanged("IsType1Visible");
                    NotifyPropertyChanged("IsType2Visible");
                }
            }
        }


        public byte EmbIndex { get; set; } = byte.MaxValue;
        public byte I_00 { get; set; }
        public byte I_02 { get; set; }
        public byte I_03 { get; set; }
        public byte FilteringMin { get; set; } //int8
        public byte FilteringMax { get; set; } //int8
        public TextureRepitition RepitionX { get; set; } //int8
        public TextureRepitition RepetitionY { get; set; } //int8
        public byte RandomSymetryX { get; set; } //int8
        public byte RandomSymetryY { get; set; } //int8

        public EMP_ScrollAnimation ScrollAnimation { get; set; }

        public void ReplaceValues(EMP_TextureSamplerDef newValues, List<IUndoRedo> undos = null)
        {
            if (undos == null) undos = new List<IUndoRedo>();

            //Utils.CopyValues only copies primitives - this must be copied manually.
            undos.Add(new UndoableProperty<EMP_TextureSamplerDef>(nameof(TextureRef), this, TextureRef, newValues.TextureRef));
            TextureRef = newValues.TextureRef;

            if(newValues.ScrollAnimation != null && ScrollAnimation != null)
            {
                undos.Add(new UndoableProperty<EMP_ScrollAnimation>(nameof(ScrollAnimation.ScrollSpeed_U), ScrollAnimation, ScrollAnimation.ScrollSpeed_U, newValues.ScrollAnimation.ScrollSpeed_U));
                undos.Add(new UndoableProperty<EMP_ScrollAnimation>(nameof(ScrollAnimation.ScrollSpeed_V), ScrollAnimation, ScrollAnimation.ScrollSpeed_V, newValues.ScrollAnimation.ScrollSpeed_V));
                undos.Add(new UndoableProperty<EMP_ScrollAnimation>(nameof(ScrollAnimation.Keyframes), ScrollAnimation, ScrollAnimation.Keyframes, newValues.ScrollAnimation.Keyframes));
                undos.Add(new UndoableProperty<EMP_ScrollAnimation>(nameof(ScrollAnimation.UseSpeedInsteadOfKeyFrames), ScrollAnimation, ScrollAnimation.UseSpeedInsteadOfKeyFrames, newValues.ScrollAnimation.UseSpeedInsteadOfKeyFrames));

                ScrollAnimation.ScrollSpeed_U = newValues.ScrollAnimation.ScrollSpeed_U;
                ScrollAnimation.ScrollSpeed_V = newValues.ScrollAnimation.ScrollSpeed_V;
                ScrollAnimation.Keyframes = newValues.ScrollAnimation.Keyframes;
                ScrollAnimation.UseSpeedInsteadOfKeyFrames = newValues.ScrollAnimation.UseSpeedInsteadOfKeyFrames;
            }

            //Copy remaining values
            undos.AddRange(Utils.CopyValues(this, newValues));

            undos.Add(new UndoActionPropNotify(this, true));
            this.NotifyPropsChanged();
        }
        
        public IEnumerable<TextureAnimationType> TextureAnimationTypes
        {
            get
            {
                return Enum.GetValues(typeof(TextureAnimationType))
                    .Cast<TextureAnimationType>();
            }
        }

        public IEnumerable<TextureRepitition> TextureRepititions
        {
            get
            {
                return Enum.GetValues(typeof(TextureRepitition))
                    .Cast<TextureRepitition>();
            }
        }

        public Visibility IsType0Visible
        {
            get { return (TextureType == TextureAnimationType.Static) ? Visibility.Visible : Visibility.Hidden; }
        }

        public Visibility IsType1Visible
        {
            get { return (TextureType == TextureAnimationType.Speed) ? Visibility.Visible : Visibility.Hidden; }
        }

        public Visibility IsType2Visible
        {
            get { return (TextureType == TextureAnimationType.SpriteSheet) ? Visibility.Visible : Visibility.Hidden; }
        }


        public enum TextureAnimationType
        {
            Static = 0,
            Speed = 1,
            SpriteSheet = 2
        }

        public enum TextureRepitition
        {
            Wrap = 0,
            Mirror = 1,
            Clamp = 2,
            Border = 3
        }

        public TextureAnimationType CalculateTextureType()
        {
            if (ScrollAnimation.UseSpeedInsteadOfKeyFrames == true)
            {
                return EMP_TextureSamplerDef.TextureAnimationType.Speed;
            }
            if (ScrollAnimation.Keyframes.Count() == 1 && ScrollAnimation.Keyframes[0].Time == -1)
            {
                return EMP_TextureSamplerDef.TextureAnimationType.Static;
            }
            else
            {
                return EMP_TextureSamplerDef.TextureAnimationType.SpriteSheet;
            }
        }
        

        public EMP_TextureSamplerDef Clone()
        {
            return new EMP_TextureSamplerDef()
            {
                I_00 = I_00,
                EmbIndex = EmbIndex,
                I_02 = I_02,
                I_03 = I_03,
                FilteringMin = FilteringMin,
                FilteringMax = FilteringMax,
                RepitionX = RepitionX,
                RepetitionY = RepetitionY,
                RandomSymetryX = RandomSymetryX,
                RandomSymetryY = RandomSymetryY,
                ScrollAnimation = ScrollAnimation.Clone(),
                TextureTypeValue = TextureTypeValue,
                TextureRef = TextureRef
            };
        }

        public static EMP_TextureSamplerDef GetNew()
        {
            AsyncObservableCollection<EMP_ScrollKeyframe> keyframes = new AsyncObservableCollection<EMP_ScrollKeyframe>();
            keyframes.Add(new EMP_ScrollKeyframe());

            return new EMP_TextureSamplerDef()
            {
                TextureType = TextureAnimationType.Static,
                RepitionX = TextureRepitition.Wrap,
                RepetitionY = TextureRepitition.Wrap,
                ScrollAnimation = new EMP_ScrollAnimation()
                {
                    Keyframes = keyframes,
                    UseSpeedInsteadOfKeyFrames = false
                }
            };
        }
        
        public bool Compare(EMP_TextureSamplerDef obj2)
        {
            return Compare(this, obj2);
        }

        public static bool Compare(EMP_TextureSamplerDef obj1, EMP_TextureSamplerDef obj2)
        {
            if (obj1.I_00 != obj2.I_00) return false;
            if (obj1.EmbIndex != obj2.EmbIndex) return false;
            if (obj1.I_02 != obj2.I_02) return false;
            if (obj1.I_03 != obj2.I_03) return false;
            if (obj1.FilteringMin != obj2.FilteringMin) return false;
            if (obj1.FilteringMax != obj2.FilteringMax) return false;
            if (obj1.RandomSymetryX != obj2.RandomSymetryX) return false;
            if (obj1.RepitionX != obj2.RepitionX) return false;
            if (obj1.RepetitionY != obj2.RepetitionY) return false;
            if (obj1.RandomSymetryY != obj2.RandomSymetryY) return false;
            if (obj1.TextureType != obj2.TextureType) return false;

            if(obj1.ScrollAnimation != null)
            {
                if (!obj1.ScrollAnimation.Compare(obj2.ScrollAnimation)) return false;
            }

            if(obj1.TextureRef != null && obj2.TextureRef != null)
            {
                if (!obj1.TextureRef.Compare(obj2.TextureRef, true)) return false;
            }
            else if (obj1.TextureRef == null && obj2.TextureRef == null)
            {

            }
            else
            {
                return false;
            }

            return true;
        }
    
        public static bool IsRepeatingTexture(EmbEntry embEntry, EffectContainer.AssetContainerTool assetContainer)
        {
            foreach(var emp in assetContainer.Assets)
            {
                if(emp.Files?.Count > 0)
                {
                    foreach (var textureDef in emp.Files[0].EmpFile.Textures)
                    {
                        if(textureDef.TextureRef == embEntry)
                        {
                            if(textureDef.SubData2 != null)
                            {
                                if (!textureDef.SubData2.useSpeedInsteadOfKeyFrames)
                                {
                                    foreach(var keyframe in textureDef.SubData2.Keyframes)
                                    {
                                        if (keyframe.ScaleX > 1f || keyframe.ScaleY > 1f) return true;
                                    }
                                }
                            }
                        }
                    }
                }
            }

            return false;
        }
    
        public static int GetSize(VersionEnum version)
        {
            return version == VersionEnum.SDBH ? ENTRY_SIZE_NEW : ENTRY_SIZE;
        }
    }

    [Serializable]
    public class EMP_ScrollAnimation
    {
        public bool UseSpeedInsteadOfKeyFrames { get; set; }
        public float ScrollSpeed_U { get; set; }
        public float ScrollSpeed_V { get; set; }

        public AsyncObservableCollection<EMP_ScrollKeyframe> Keyframes { get; set; }

        public EMP_ScrollAnimation Clone()
        {
            AsyncObservableCollection<EMP_ScrollKeyframe> _Keyframes = AsyncObservableCollection<EMP_ScrollKeyframe>.Create();

            if(Keyframes != null)
            {
                foreach (var e in Keyframes)
                {
                    _Keyframes.Add(e.Clone());
                }
            }

            return new EMP_ScrollAnimation()
            {
                UseSpeedInsteadOfKeyFrames = UseSpeedInsteadOfKeyFrames,
                ScrollSpeed_U = ScrollSpeed_U,
                ScrollSpeed_V = ScrollSpeed_V,
                Keyframes = _Keyframes
            };
        }

        public bool Compare(EMP_ScrollAnimation obj2)
        {
            return Compare(this, obj2);
        }

        public static bool Compare(EMP_ScrollAnimation obj1, EMP_ScrollAnimation obj2)
        {
            if (obj2 == null && obj1 == null) return true;
            if (obj1 != null && obj2 == null) return false;
            if (obj2 != null && obj1 == null) return false;

            if (obj1.UseSpeedInsteadOfKeyFrames != obj2.UseSpeedInsteadOfKeyFrames) return false;
            if (obj1.ScrollSpeed_U != obj2.ScrollSpeed_U) return false;
            if (obj1.ScrollSpeed_V != obj2.ScrollSpeed_V) return false;

            if(obj1.Keyframes != null && obj2.Keyframes != null)
            {
                if (obj1.Keyframes.Count != obj2.Keyframes.Count) return false;

                for(int i = 0; i < obj1.Keyframes.Count; i++)
                {
                    if (!obj1.Keyframes[i].Compare(obj2.Keyframes[i])) return false;
                }
            }
            else if(obj1.Keyframes == null && obj2.Keyframes == null)
            {
                //Both are null. OK
            }
            else
            {
                //Mismatch
                return false;
            }

            return true;
        }
    }

    [Serializable]
    public class EMP_ScrollKeyframe
    {
        public int Time { get; set; }
        public float ScrollX { get; set; }
        public float ScrollY { get; set; }
        public float ScaleX { get; set; }
        public float ScaleY { get; set; }

        //Added in newer versions of EMP (SDBH and Breakers)
        public string F_20 { get; set; } //float
        public string F_24 { get; set; } //float

        public EMP_ScrollKeyframe Clone()
        {
            return new EMP_ScrollKeyframe()
            {
                Time = Time,
                ScaleX = ScaleX,
                ScrollY = ScrollY,
                ScrollX = ScrollX,
                ScaleY = ScaleY,
                F_20 = "0.0",
                F_24 = "0.0"
            };
        }

        public void SetDefaultValuesForSDBH()
        {
            if (F_20 == null)
                F_20 = "0.0";
            if (F_24 == null)
                F_24 = "0.0";
        }

        public bool Compare(EMP_ScrollKeyframe obj2)
        {
            return Compare(this, obj2);
        }

        public static bool Compare(EMP_ScrollKeyframe obj1, EMP_ScrollKeyframe obj2)
        {
            if (obj1.Time != obj2.Time) return false;
            if (obj1.ScrollX != obj2.ScrollX) return false;
            if (obj1.ScrollY != obj2.ScrollY) return false;
            if (obj1.ScaleX != obj2.ScaleX) return false;
            if (obj1.ScaleY != obj2.ScaleY) return false;
            if (obj1.F_20 != obj2.F_20) return false;
            if (obj1.F_24 != obj2.F_24) return false;

            return true;
        }
    }

    [Serializable]
    public class TextureEntry_Ref : INotifyPropertyChanged
    {
        #region NotifyPropChanged
        [field: NonSerialized]
        public event PropertyChangedEventHandler PropertyChanged;

        public void NotifyPropertyChanged(String propertyName = "")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
        #endregion

        private EMP_TextureSamplerDef _textureRef = null;
        public EMP_TextureSamplerDef TextureRef
        {
            get
            {
                return _textureRef;
            }
            set
            {
                if (value != _textureRef)
                {
                    _textureRef = value;
                    NotifyPropertyChanged(nameof(TextureRef));
                    NotifyPropertyChanged(nameof(UndoableTextureRef));
                }
            }
        }
    
        public EMP_TextureSamplerDef UndoableTextureRef
        {
            get
            {
                return TextureRef;
            }
            set
            {
                if(TextureRef != value)
                {
                    UndoManager.Instance.AddUndo(new UndoableProperty<TextureEntry_Ref>(nameof(TextureRef), this, TextureRef, value, "Texture Ref"));
                    TextureRef = value;
                    NotifyPropertyChanged(nameof(TextureRef));
                    NotifyPropertyChanged(nameof(UndoableTextureRef));
                }
            }
        }
   
        public TextureEntry_Ref() { }

        public TextureEntry_Ref(EMP_TextureSamplerDef textureSampler)
        {
            _textureRef = textureSampler;
        }
    }

    //Interface
    public interface IKeyframe
    {
        ushort Time { get; set; }
        float Value { get; set; }
    }

    /// <summary>
    /// The exact node type. Includes all Emitters and Emissions.
    /// </summary>
    public enum NodeSpecificType
    {
        Null,
        VerticalDistribution,
        SphericalDistribution,
        ShapePerimeterDistribution,
        ShapeAreaDistribution,
        AutoOriented,
        AutoOriented_VisibleOnSpeed,
        Default,
        ConeExtrude,
        Mesh,
        ShapeDraw
    }

    public enum KeyframedValueType
    {
        //All known keyframed values and their associated Parameter/Component values. These apply to main KeyframedValues only, not the groups (which are unknown so far).
        //Formated where the first number is the Parameter, and any following are the Components

        Position, //0, X = 0, Y = 1, Z = 2 (All node types)
        Rotation, //1, X = 0, Y = 1, Z = 2 (All node types)
        ScaleBase, //When UseScale2, its 2, 2, otherwise its 2, 0 (All Emisions)
        ScaleXY, //When UseScale2, its 2, X = 0, Y = 1, otherwise its not used (All Emissions)
        Color1, //3, R=0, G=1, B=2 (All Emissions)
        Color2, //4, R=0, G=1, B=2, A=3 (All Emissions)
        Color1_Transparency, //3, 3 (All Emissions)
        Color2_Transparency, //4, 3 (All Emissions)
        ActiveRotation, //1, 3 (AutoOriented, Default, Mesh, ShapeDraw)
        PositionY, //2, 0 (ShapeAreaDist, ShapePerimeterDist, VerticalDist)
        Velocity, //2, 1 (ShapeAreaDist, ShapePerimeterDist, SphereDist, VerticalDist)
        Angle, //2, 2 (ShapeAreaDist, ShapePerimeterDist, VerticalDist)
        Size1, //3, 0 (ShapeAreaDist, ShapePerimeterDist) OR 2, 0 (SphereDist)
        Size2, //3, 1 (ShapeAreaDist, ShapePerimeterDist)
    }
}