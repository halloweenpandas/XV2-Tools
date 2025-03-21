﻿using MahApps.Metro.Controls;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using Xv2CoreLib.EEPK;
using Xv2CoreLib.EffectContainer;
using Xv2CoreLib.EMB_CLASS;
using Xv2CoreLib.EMM;
using Xv2CoreLib.EMP_NEW;
using Xv2CoreLib.ECF;
using Xv2CoreLib.ETR;
using Xv2CoreLib.HslColor;
using Xv2CoreLib.Resource.App;
using Xv2CoreLib.Resource.UndoRedo;

namespace EEPK_Organiser.Forms.Recolor
{
    /// <summary>
    /// Interaction logic for RecolorAll_HueSet.xaml
    /// </summary>
    public partial class RecolorAll_HueSet : MetroWindow, INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        private void NotifyPropertyChanged(String propertyName = "")
        {
            if (PropertyChanged != null)
            {
                PropertyChanged(this, new PropertyChangedEventArgs(propertyName));
            }
        }

        private enum Mode
        {
            Asset,
            Material,
            Global,
            ParticleNode,
            TraceNode,
            ColorFadeNode
        }

        private AssetType assetType = AssetType.EMO;
        private Asset asset = null;
        private EmmMaterial material = null;
        private EffectContainerFile effectContainerFile = null;
        private ParticleNode particleNode = null;
        private ETR_Node etrNode = null;
        private ECF_Node ecfNode = null;

        private Mode currentMode = Mode.Asset;

        //Values
        private double initialHue = 0;
        private double hueChange = 0;
        private int _variance = 0;
        private bool _textureVariance = false;


        private RgbColor _rgbColor = new RgbColor(255, 255, 255);
        public RgbColor rgbColor
        {
            get
            {
                return this._rgbColor;
            }
            set
            {
                if (value != this._rgbColor)
                {
                    this._rgbColor = value;
                    NotifyPropertyChanged("rgbColor");
                    NotifyPropertyChanged("preview");
                }
            }
        }
        private HslColor _hslColor = null;
        public HslColor hslColor
        {
            get
            {
                return this._hslColor;
            }
            set
            {
                if (value != this._hslColor)
                {
                    this._hslColor = value;
                    NotifyPropertyChanged("hslColor");
                }
            }
        }
        public int Variance
        {
            get
            {
                return this._variance;
            }
            set
            {
                if (value != this._variance)
                {
                    this._variance = value;
                    NotifyPropertyChanged(nameof(Variance));
                }
            }
        }
        public bool TextureVariance
        {
            get
            {
                return _textureVariance;
            }
            set
            {
                if(value != _textureVariance)
                {
                    _textureVariance = value;
                    NotifyPropertyChanged(nameof(TextureVariance));
                }
            }
        }

        public Brush preview
        {
            get
            {
                return new SolidColorBrush(Color.FromArgb(255, rgbColor.R_int, rgbColor.G_int, rgbColor.B_int));
            }
        }

        #region Tooltips
        public string HueRevertTooltip { get { return string.Format("Revert to original value of {0}", initialHue); } }
        public string RgbPreviewTooltip { get { return string.Format("R: {0} ({3}), G: {1} ({4}), B: {2} ({5})", rgbColor.R, rgbColor.G, rgbColor.B, rgbColor.R_int, rgbColor.G_int, rgbColor.B_int); } }
        #endregion

        /// <summary>
        /// Hue set a asset.
        /// </summary>
        public RecolorAll_HueSet(AssetType _assetType, Asset _asset, Window parent)
        {
            currentMode = Mode.Asset;
            assetType = _assetType;
            asset = _asset;

            InitializeComponent();
            Owner = parent;
            DataContext = this;
        }

        /// <summary>
        /// Hue set a material.
        /// </summary>
        /// <param name="_material"></param>
        public RecolorAll_HueSet(EmmMaterial _material, Window parent)
        {
            currentMode = Mode.Material;
            material = _material;

            InitializeComponent();
            Owner = parent;
            DataContext = this;
        }

        /// <summary>
        /// Hue set all assets, materials and textures in a EffectContainerFile.
        /// </summary>
        public RecolorAll_HueSet(EffectContainerFile _effectContainerFile, Window parent)
        {
            currentMode = Mode.Global;
            effectContainerFile = _effectContainerFile;
            InitializeComponent();
            Owner = parent;
            DataContext = this;
        }

        /// <summary>
        /// Hue set a ParticleEffect.
        /// </summary>
        public RecolorAll_HueSet(ParticleNode node, Window parent)
        {
            currentMode = Mode.ParticleNode;
            particleNode = node;

            InitializeComponent();
            Owner = parent;
            DataContext = this;
        }

        public RecolorAll_HueSet(ETR_Node node, Window parent)
        {
            currentMode = Mode.TraceNode;
            etrNode = node;

            InitializeComponent();
            Owner = parent;
            DataContext = this;
        }

        public RecolorAll_HueSet(ECF_Node node, Window parent)
        {
            currentMode = Mode.ColorFadeNode;
            ecfNode = node;

            InitializeComponent();
            Owner = parent;
            DataContext = this;
        }

        public bool Initialize()
        {
            if (((currentMode == Mode.Asset && assetType == AssetType.EMO) || currentMode == Mode.Global) && !SettingsManager.Instance.LoadTextures)
            {
                MessageBox.Show("This option is not available while textures are turned off. Enable Load Textures in the settings to use this option.", "Not Available", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            List<RgbColor> colors = null;

            if (currentMode == Mode.Asset)
            {
                colors = asset.GetUsedColors();
            }
            else if (currentMode == Mode.Material)
            {
                colors = material.GetUsedColors();
            }
            else if (currentMode == Mode.Global)
            {
                colors = GetUsedColorsByEverything();
            }
            else if (currentMode == Mode.ParticleNode)
            {
                colors = particleNode.GetUsedColors();
            }
            else if (currentMode == Mode.TraceNode)
            {
                colors = etrNode.GetUsedColors();
            }
            else if (currentMode == Mode.ColorFadeNode)
            {
                colors = ecfNode.GetUsedColors();
            }


            if (colors.Count == 0)
            {
                MessageBox.Show("No color information was found on this asset so it cannot be hue shifted.\n\nThe most likely cause of this is that all color sources for this asset were either all white (1,1,1) or all black (0,0,0).", "No color information", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            rgbColor = ColorEx.GetAverageColor(colors);
            hslColor = rgbColor.ToHsl();

            initialHue = hslColor.Hue;

            ValueChanged();

            return true;
        }

        private void Slider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            ValueChanged();
        }

        private void IntegerUpDown_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double?> e)
        {
            ValueChanged();
        }

        private void ValueChanged()
        {
            rgbColor = hslColor.ToRgb();
            NotifyPropertyChanged("HueRevertTooltip");
            NotifyPropertyChanged("SaturationRevertTooltip");
            NotifyPropertyChanged("LightnessRevertTooltip");
            NotifyPropertyChanged("RgbPreviewTooltip");
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            List<IUndoRedo> undos = new List<IUndoRedo>();
            object context = null;

            hueChange = hslColor.Hue;

            if (currentMode == Mode.Asset)
            {
                ChangeHueForAsset(asset, undos);
                context = asset;
            }
            else if (currentMode == Mode.Material)
            {
                material.ChangeHsl(hueChange, 0f, 0f, undos, true, Variance);
            }
            else if (currentMode == Mode.Global)
            {
                ChangeHueForEverything(undos);
                context = effectContainerFile;
            }
            else if (currentMode == Mode.ParticleNode)
            {
                particleNode.ChangeHue(hueChange, 0f, 0f, undos, true, Variance);
                context = particleNode;
            }
            else if (currentMode == Mode.TraceNode)
            {
                etrNode.ChangeHue(hueChange, 0f, 0f, undos, true, Variance);
                context = etrNode;
            }
            else if (currentMode == Mode.ColorFadeNode)
            {
                ecfNode.ChangeHue(hueChange, 0f, 0f, undos, true, Variance);
                context = ecfNode;
            }

            UndoManager.Instance.AddUndo(new CompositeUndo(undos, "Hue Set"), UndoGroup.ColorControl, null, context);
            UndoManager.Instance.ForceEventCall(UndoGroup.ColorControl, null, context);

            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }


        private void ChangeHueForAsset(Asset _asset, List<IUndoRedo> undos)
        {
            switch (_asset.assetType)
            {
                case AssetType.PBIND:
                    _asset.Files[0].EmpFile.ChangeHue(hueChange, 0f, 0f, undos, true, Variance);
                    break;
                case AssetType.TBIND:
                    _asset.Files[0].EtrFile.ChangeHue(hueChange, 0f, 0f, undos, true, Variance);
                    break;
                case AssetType.CBIND:
                    _asset.Files[0].EcfFile.ChangeHue(hueChange, 0f, 0f, undos, true, Variance);
                    break;
                case AssetType.LIGHT:
                    _asset.Files[0].EmaFile.ChangeHue(hueChange, 0f, 0f, undos, true, Variance);
                    break;
                case AssetType.EMO:
                    foreach (var file in _asset.Files)
                    {
                        switch (file.Extension)
                        {
                            case ".emb":
                                file.EmbFile.ChangeHue(hueChange, 0f, 0f, undos, true, TextureVariance ? Variance : 0); //No lightness change
                                break;
                            case ".emm":
                                file.EmmFile.ChangeHsl(hueChange, 0f, 0f, undos, true, Variance);
                                break;
                            case ".mat.ema":
                                EMM_File emmFile = _asset.Files.FirstOrDefault(x => x.fileType == EffectFile.FileType.EMM)?.EmmFile;
                                file.EmaFile.ChangeHue(hueChange, 0f, 0f, undos, true, Variance, emmFile);
                                break;
                        }
                    }
                    break;
            }

        }

        private List<RgbColor> GetUsedColorsByEverything()
        {
            List<RgbColor> colors = new List<RgbColor>();

            colors.AddRange(GetUsedColersByContainer(effectContainerFile.Pbind));
            colors.AddRange(GetUsedColersByContainer(effectContainerFile.Tbind));
            colors.AddRange(GetUsedColersByContainer(effectContainerFile.Cbind));
            colors.AddRange(GetUsedColersByContainer(effectContainerFile.LightEma));
            colors.AddRange(GetUsedColersByContainer(effectContainerFile.Emo));
            colors.AddRange(effectContainerFile.Pbind.File3_Ref.GetUsedColors());
            colors.AddRange(effectContainerFile.Tbind.File3_Ref.GetUsedColors());
            colors.AddRange(effectContainerFile.Pbind.File2_Ref.GetUsedColors());
            colors.AddRange(effectContainerFile.Tbind.File2_Ref.GetUsedColors());

            return colors;
        }

        private List<RgbColor> GetUsedColersByContainer(AssetContainerTool container)
        {
            List<RgbColor> colors = new List<RgbColor>();

            foreach (var asset in container.Assets)
            {
                colors.AddRange(asset.GetUsedColors());
            }

            return colors;
        }

        private void ChangeHueForEverything(List<IUndoRedo> undos)
        {
            ChangeHueForContainer(effectContainerFile.Pbind, undos);
            ChangeHueForContainer(effectContainerFile.Tbind, undos);
            ChangeHueForContainer(effectContainerFile.Cbind, undos);
            ChangeHueForContainer(effectContainerFile.Emo, undos);
            ChangeHueForContainer(effectContainerFile.LightEma, undos);
            effectContainerFile.Pbind.File3_Ref.ChangeHue(hueChange, 0f, 0f, undos, true, TextureVariance ? Variance : 0);
            effectContainerFile.Tbind.File3_Ref.ChangeHue(hueChange, 0f, 0f, undos, true, TextureVariance ? Variance : 0);
            effectContainerFile.Pbind.File2_Ref.ChangeHsl(hueChange, 0f, 0f, undos, true, Variance);
            effectContainerFile.Tbind.File2_Ref.ChangeHsl(hueChange, 0f, 0f, undos, true, Variance);
        }

        private void ChangeHueForContainer(AssetContainerTool container, List<IUndoRedo> undos)
        {
            foreach (var _asset in container.Assets)
            {
                ChangeHueForAsset(_asset, undos);
            }
        }

        private void Button_UndoHueChange_Click(object sender, RoutedEventArgs e)
        {
            hslColor.Hue = initialHue;
            ValueChanged();
        }

    }
}
