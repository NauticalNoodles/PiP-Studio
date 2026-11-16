using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace PipStudio
{
    // ---------------------------------------------------------------------------------
    //  Rows shown in the tables (public so WPF data binding can read them)
    // ---------------------------------------------------------------------------------
    public class WinRowItem
    {
        public IntPtr Hwnd { get; set; }
        public bool IsDisplay { get; set; }
        public string Title { get; set; }
        public string App { get; set; }
        public string SizeText { get; set; }
        public string Status { get; set; }
        public ImageSource Icon { get; set; }
        public string Glyph { get; set; }
        public bool IsAuto { get; set; }
        public bool MayFreeze { get; set; }
        public Visibility FreezeVis { get { return MayFreeze ? Visibility.Visible : Visibility.Collapsed; } }
        internal System.Drawing.Rectangle ScreenBounds;
        internal int Order;
    }

    public class PipRowItem
    {
        public string Title { get; set; }
        public string App { get; set; }
        public string Position { get; set; }
        public string SizeText { get; set; }
        public string OpacityText { get; set; }
        public string Mode { get; set; }
        public ImageSource Icon { get; set; }
        public string Glyph { get; set; }
        internal PipWindow Pip;
        internal IntPtr External;          // another app's own PiP window (Chrome, Firefox, FreeTube...)
    }

    class SearchEntry
    {
        public string Title, Text, Page;
        public FrameworkElement Element;
        public Action Reveal;          // opens the expander it lives in
    }

    class CardParts
    {
        public Border Root;
        public TextBlock Title, Desc;
    }

    // =================================================================================
    //  The main window — built to the Windows 11 Settings / Task Manager design:
    //  Mica, NavigationView metrics with a sliding selection pill, a rounded content layer,
    //  SettingsCard / SettingsExpander rows, Task Manager tables and Fluent motion.
    // =================================================================================
    static partial class MainUI
    {
        static Window w;
        static bool exiting, closedHintShown;

        static ListBox winList, runList, autoList;
        static StackPanel markedList;          // Running PiPs > Marked as PiPs
        static Border markedBox;
        static string markedSig = "";
        static bool syncingSel, sizing;
        static string autoSig = "";
        static Border navPane, previewBox, chromiumCard, detailsCard, contentLayer;
        static readonly Dictionary<string, bool> chromiumExe = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        static StackPanel pageHeader;
        static bool detailsWanted = true;
        static TextBlock pageTitle, searchHint, padStatus, selTitle, selSub, runEmpty, fixState, previewNote;
        static TextBox searchBox, winSearch;
        static TextBlock winSearchHint;
        static Grid searchWrap;
        static WrapPanel cmdBar;
        static Grid pageWindows, pageRunning, winHeader, runHeader, navRoot;
        static ScrollViewer pageScroll;
        static StackPanel settingsHost;
        static RadioButton[] navBtns;
        static Button burger, searchIconBtn;
        static Rectangle indicator;
        static Canvas indCanvas;
        static double indTop = -1;
        static Wpf.Ui.Controls.ToggleSwitch fixAlways;
        static Button fixNow;
        static readonly Dictionary<string, StackPanel> pages = new Dictionary<string, StackPanel>();
        static readonly List<SearchEntry> searchIndex = new List<SearchEntry>();
        static readonly string[] navKeys = { "windows", "running", "appearance", "behavior", "controller", "shortcuts", "settings", "phone" };
        static readonly string[] navLabels = { "Windows", "Running PiPs", "Appearance", "Behavior", "Controller", "Shortcuts", "Settings", "Phone" };
        static string page = "windows", buildingPage, lastSettingsPage = "appearance";
        static Action buildingReveal;
        static bool compact, userCompact, overlayOpen, paneStyled, lastExpanded = true, fixSync;
        static Border navSpace, scrim;
        static readonly Dictionary<FrameworkElement, int> widthTokens = new Dictionary<FrameworkElement, int>();
        static int fixToken;
        static DispatcherTimer tick;
        static int tickCount;
        static readonly Dictionary<IntPtr, ImageSource> iconCache = new Dictionary<IntPtr, ImageSource>();
        static string winSig = "", runSig = "", sortKey = "", winFilter = "";
        static bool sortDesc;

        static IntPtr thumb, thumbSrc;
        static System.Drawing.Rectangle thumbRect;
        static int thumbAlpha;

        static string captureKb;
        static Button captureKbBtn;
        static Action refreshBindings;
        static Action refreshUpdates;   // the Updates card on the settings page, while the window is open
        static Action refreshCompanion; // the Phone companion section, while the window is open

        public static void Init()
        {
            App.OpenMain = Open;
            App.CloseMain = () => { if (w != null && !exiting) { exiting = true; w.Close(); } };
            Updater.Changed += () => { var r = refreshUpdates; if (r != null) r(); };
            Companion.Changed += () => { var r = refreshCompanion; if (r != null) r(); };
            Theme.Changed += () => { if (w != null) Looks.Apply(w); };   // keeps the look's colors on top after a light/dark or accent change
        }

        // ----------------------------------------------------------------------------- XAML
        /// <summary>Styles shared by the main window and the window picker, so both look the same: page title, captions,
        /// table cells and the list rows (hover, selection and the accent bar).</summary>
        internal const string SharedStyles = @"
    <Style x:Key='PageTitle' TargetType='TextBlock'><Setter Property='FontFamily' Value='Segoe UI Variable Display, Segoe UI, Microsoft YaHei UI'/><Setter Property='FontSize' Value='28'/><Setter Property='FontWeight' Value='SemiBold'/><Setter Property='Foreground' Value='{DynamicResource Text}'/></Style>
    <Style x:Key='Caption' TargetType='TextBlock'><Setter Property='FontSize' Value='12'/><Setter Property='Foreground' Value='{DynamicResource Sub}'/><Setter Property='TextWrapping' Value='Wrap'/></Style>
    <Style x:Key='Cell' TargetType='TextBlock'><Setter Property='FontSize' Value='13'/><Setter Property='Foreground' Value='{DynamicResource Text}'/><Setter Property='VerticalAlignment' Value='Center'/><Setter Property='TextTrimming' Value='CharacterEllipsis'/></Style>
    <Style x:Key='CellSub' TargetType='TextBlock' BasedOn='{StaticResource Cell}'><Setter Property='Foreground' Value='{DynamicResource Sub}'/></Style>
    <Style x:Key='RowItem' TargetType='ListBoxItem'>
      <Setter Property='HorizontalContentAlignment' Value='Stretch'/><Setter Property='FocusVisualStyle' Value='{x:Null}'/><Setter Property='Foreground' Value='{DynamicResource Text}'/>
      <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='ListBoxItem'>
        <Grid Margin='0,1' Background='Transparent'>
          <Border x:Name='sel' CornerRadius='4' Background='{DynamicResource NavSel}' Opacity='0'/>
          <Border x:Name='hov' CornerRadius='4' Background='{DynamicResource Hover}' Opacity='0'/>
          <Rectangle x:Name='bar' Width='3' Height='16' RadiusX='1.5' RadiusY='1.5' Fill='{DynamicResource Accent}' HorizontalAlignment='Left' VerticalAlignment='Center' RenderTransformOrigin='0.5,0.5' Opacity='0'>
            <Rectangle.RenderTransform><ScaleTransform ScaleY='0.3'/></Rectangle.RenderTransform>
          </Rectangle>
          <ContentPresenter Margin='14,0,8,0' VerticalAlignment='Center'/>
        </Grid>
        <ControlTemplate.Triggers>
          <Trigger Property='IsMouseOver' Value='True'>
            <Trigger.EnterActions><BeginStoryboard><Storyboard><DoubleAnimation Storyboard.TargetName='hov' Storyboard.TargetProperty='Opacity' To='1' Duration='0:0:0.083'/></Storyboard></BeginStoryboard></Trigger.EnterActions>
            <Trigger.ExitActions><BeginStoryboard><Storyboard><DoubleAnimation Storyboard.TargetName='hov' Storyboard.TargetProperty='Opacity' To='0' Duration='0:0:0.167'/></Storyboard></BeginStoryboard></Trigger.ExitActions>
          </Trigger>
          <Trigger Property='IsSelected' Value='True'>
            <Trigger.EnterActions><BeginStoryboard><Storyboard>
              <DoubleAnimation Storyboard.TargetName='sel' Storyboard.TargetProperty='Opacity' To='1' Duration='0:0:0.083'/>
              <DoubleAnimation Storyboard.TargetName='bar' Storyboard.TargetProperty='Opacity' To='1' Duration='0:0:0.083'/>
              <DoubleAnimation Storyboard.TargetName='bar' Storyboard.TargetProperty='(UIElement.RenderTransform).(ScaleTransform.ScaleY)' To='1' Duration='0:0:0.3'><DoubleAnimation.EasingFunction><QuinticEase EasingMode='EaseOut'/></DoubleAnimation.EasingFunction></DoubleAnimation>
            </Storyboard></BeginStoryboard></Trigger.EnterActions>
            <Trigger.ExitActions><BeginStoryboard><Storyboard>
              <DoubleAnimation Storyboard.TargetName='sel' Storyboard.TargetProperty='Opacity' To='0' Duration='0:0:0.167'/>
              <DoubleAnimation Storyboard.TargetName='bar' Storyboard.TargetProperty='Opacity' To='0' Duration='0:0:0.1'/>
              <DoubleAnimation Storyboard.TargetName='bar' Storyboard.TargetProperty='(UIElement.RenderTransform).(ScaleTransform.ScaleY)' To='0.3' Duration='0:0:0.1'/>
            </Storyboard></BeginStoryboard></Trigger.ExitActions>
          </Trigger>
        </ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter>
    </Style>
";

        const string Xaml = @"
<ui:FluentWindow xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
        xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'
        xmlns:ui='http://schemas.lepo.co/wpfui/2022/xaml'
        Title='PiP Studio' Width='1120' Height='740' MinWidth='520' MinHeight='420' UseLayoutRounding='True'
        FontFamily='Segoe UI Variable Text, Segoe UI' FontSize='14'
        ExtendsContentIntoTitleBar='True' WindowBackdropType='Mica' WindowStartupLocation='CenterScreen'>
  <ui:FluentWindow.Resources>
" + SharedStyles + @"    <GridLength x:Key='WColApp'>150</GridLength><GridLength x:Key='WColSize'>110</GridLength><GridLength x:Key='WColStatus'>110</GridLength>
    <GridLength x:Key='RColApp'>130</GridLength><GridLength x:Key='RColPos'>110</GridLength><GridLength x:Key='RColSize'>100</GridLength><GridLength x:Key='RColOpacity'>70</GridLength><GridLength x:Key='RColMode'>170</GridLength>

    <Style x:Key='SliderFill' TargetType='RepeatButton'><Setter Property='Focusable' Value='False'/><Setter Property='Template'><Setter.Value><ControlTemplate TargetType='RepeatButton'>
      <Grid Background='Transparent' Height='32'><Border Height='4' CornerRadius='2' Background='{DynamicResource Accent}' VerticalAlignment='Center'/></Grid></ControlTemplate></Setter.Value></Setter></Style>
    <Style x:Key='SliderEmpty' TargetType='RepeatButton'><Setter Property='Focusable' Value='False'/><Setter Property='Template'><Setter.Value><ControlTemplate TargetType='RepeatButton'>
      <Grid Background='Transparent' Height='32'><Border Height='4' CornerRadius='2' Background='{DynamicResource TrackBrush}' VerticalAlignment='Center'/></Grid></ControlTemplate></Setter.Value></Setter></Style>
    <Style x:Key='SliderThumb' TargetType='Thumb'><Setter Property='Template'><Setter.Value><ControlTemplate TargetType='Thumb'>
      <Grid Width='20' Height='20' Background='Transparent'>
        <Ellipse Fill='{DynamicResource Tile}' Stroke='{DynamicResource CardBorder}' StrokeThickness='1'/>
        <Ellipse x:Name='dot' Width='12' Height='12' Fill='{DynamicResource Accent}' RenderTransformOrigin='0.5,0.5'><Ellipse.RenderTransform><ScaleTransform ScaleX='1' ScaleY='1'/></Ellipse.RenderTransform></Ellipse>
      </Grid>
      <ControlTemplate.Triggers>
        <Trigger Property='IsMouseOver' Value='True'>
          <Trigger.EnterActions><BeginStoryboard><Storyboard>
            <DoubleAnimation Storyboard.TargetName='dot' Storyboard.TargetProperty='(UIElement.RenderTransform).(ScaleTransform.ScaleX)' To='1.17' Duration='0:0:0.083'/>
            <DoubleAnimation Storyboard.TargetName='dot' Storyboard.TargetProperty='(UIElement.RenderTransform).(ScaleTransform.ScaleY)' To='1.17' Duration='0:0:0.083'/>
          </Storyboard></BeginStoryboard></Trigger.EnterActions>
          <Trigger.ExitActions><BeginStoryboard><Storyboard>
            <DoubleAnimation Storyboard.TargetName='dot' Storyboard.TargetProperty='(UIElement.RenderTransform).(ScaleTransform.ScaleX)' To='1' Duration='0:0:0.167'/>
            <DoubleAnimation Storyboard.TargetName='dot' Storyboard.TargetProperty='(UIElement.RenderTransform).(ScaleTransform.ScaleY)' To='1' Duration='0:0:0.167'/>
          </Storyboard></BeginStoryboard></Trigger.ExitActions>
        </Trigger>
        <Trigger Property='IsDragging' Value='True'>
          <Trigger.EnterActions><BeginStoryboard><Storyboard>
            <DoubleAnimation Storyboard.TargetName='dot' Storyboard.TargetProperty='(UIElement.RenderTransform).(ScaleTransform.ScaleX)' To='0.83' Duration='0:0:0.083'/>
            <DoubleAnimation Storyboard.TargetName='dot' Storyboard.TargetProperty='(UIElement.RenderTransform).(ScaleTransform.ScaleY)' To='0.83' Duration='0:0:0.083'/>
          </Storyboard></BeginStoryboard></Trigger.EnterActions>
          <Trigger.ExitActions><BeginStoryboard><Storyboard>
            <DoubleAnimation Storyboard.TargetName='dot' Storyboard.TargetProperty='(UIElement.RenderTransform).(ScaleTransform.ScaleX)' To='1' Duration='0:0:0.167'/>
            <DoubleAnimation Storyboard.TargetName='dot' Storyboard.TargetProperty='(UIElement.RenderTransform).(ScaleTransform.ScaleY)' To='1' Duration='0:0:0.167'/>
          </Storyboard></BeginStoryboard></Trigger.ExitActions>
        </Trigger>
      </ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter></Style>
    <Style TargetType='Slider'>
      <Setter Property='MinHeight' Value='32'/><Setter Property='IsMoveToPointEnabled' Value='True'/>
      <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='Slider'>
        <Grid Background='Transparent' SnapsToDevicePixels='True'>
          <Track x:Name='PART_Track'>
            <Track.DecreaseRepeatButton><RepeatButton Command='Slider.DecreaseLarge' Style='{StaticResource SliderFill}'/></Track.DecreaseRepeatButton>
            <Track.IncreaseRepeatButton><RepeatButton Command='Slider.IncreaseLarge' Style='{StaticResource SliderEmpty}'/></Track.IncreaseRepeatButton>
            <Track.Thumb><Thumb Style='{StaticResource SliderThumb}'/></Track.Thumb>
          </Track>
        </Grid></ControlTemplate></Setter.Value></Setter>
    </Style>

    <!-- NavigationViewItem: 36 high, 4 px radius, fading hover; the selection pill is a separate element that slides -->
    <Style x:Key='Nav' TargetType='RadioButton'>
      <Setter Property='Foreground' Value='{DynamicResource Text}'/><Setter Property='FontSize' Value='14'/><Setter Property='FocusVisualStyle' Value='{x:Null}'/>
      <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='RadioButton'>
        <Grid Margin='4,2' Height='36' Background='Transparent' Cursor='Hand'>
          <Border x:Name='sel' CornerRadius='4' Background='{DynamicResource NavSel}' Opacity='0'/>
          <Border x:Name='hov' CornerRadius='4' Background='{DynamicResource Hover}' Opacity='0'/>
          <StackPanel x:Name='content' Orientation='Horizontal'>
            <TextBlock Text='{TemplateBinding Tag}' FontFamily='Segoe Fluent Icons, Segoe MDL2 Assets' FontSize='16' Width='40' TextAlignment='Center' VerticalAlignment='Center'/>
            <ContentPresenter Margin='4,0,12,0' VerticalAlignment='Center'/>
          </StackPanel>
        </Grid>
        <ControlTemplate.Triggers>
          <Trigger Property='IsMouseOver' Value='True'>
            <Trigger.EnterActions><BeginStoryboard><Storyboard><DoubleAnimation Storyboard.TargetName='hov' Storyboard.TargetProperty='Opacity' To='1' Duration='0:0:0.083'/></Storyboard></BeginStoryboard></Trigger.EnterActions>
            <Trigger.ExitActions><BeginStoryboard><Storyboard><DoubleAnimation Storyboard.TargetName='hov' Storyboard.TargetProperty='Opacity' To='0' Duration='0:0:0.167'/></Storyboard></BeginStoryboard></Trigger.ExitActions>
          </Trigger>
          <Trigger Property='IsChecked' Value='True'>
            <Trigger.EnterActions><BeginStoryboard><Storyboard><DoubleAnimation Storyboard.TargetName='sel' Storyboard.TargetProperty='Opacity' To='1' Duration='0:0:0.083'/></Storyboard></BeginStoryboard></Trigger.EnterActions>
            <Trigger.ExitActions><BeginStoryboard><Storyboard><DoubleAnimation Storyboard.TargetName='sel' Storyboard.TargetProperty='Opacity' To='0' Duration='0:0:0.167'/></Storyboard></BeginStoryboard></Trigger.ExitActions>
          </Trigger>
          <Trigger Property='IsPressed' Value='True'><Setter TargetName='content' Property='Opacity' Value='0.7'/></Trigger>
        </ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter>
    </Style>

    <!-- Plain subtle button (hamburger, search icon): fading hover, 4 px radius -->
    <Style x:Key='Subtle' TargetType='Button'>
      <Setter Property='Foreground' Value='{DynamicResource Text}'/><Setter Property='FocusVisualStyle' Value='{x:Null}'/>
      <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='Button'>
        <Grid Background='Transparent' Cursor='Hand'>
          <Border x:Name='hov' CornerRadius='4' Background='{DynamicResource Hover}' Opacity='0'/>
          <ContentPresenter x:Name='cp' HorizontalAlignment='Center' VerticalAlignment='Center'/>
        </Grid>
        <ControlTemplate.Triggers>
          <Trigger Property='IsMouseOver' Value='True'>
            <Trigger.EnterActions><BeginStoryboard><Storyboard><DoubleAnimation Storyboard.TargetName='hov' Storyboard.TargetProperty='Opacity' To='1' Duration='0:0:0.083'/></Storyboard></BeginStoryboard></Trigger.EnterActions>
            <Trigger.ExitActions><BeginStoryboard><Storyboard><DoubleAnimation Storyboard.TargetName='hov' Storyboard.TargetProperty='Opacity' To='0' Duration='0:0:0.167'/></Storyboard></BeginStoryboard></Trigger.ExitActions>
          </Trigger>
          <Trigger Property='IsPressed' Value='True'><Setter TargetName='cp' Property='Opacity' Value='0.6'/></Trigger>
        </ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter>
    </Style>

    <!-- Table rows (Task Manager): fading hover, selection fill with an accent pill that grows in -->

    <DataTemplate x:Key='AutoRow'>
      <Grid MinHeight='54'>
        <Grid.ColumnDefinitions><ColumnDefinition Width='28'/><ColumnDefinition Width='*'/><ColumnDefinition Width='Auto'/></Grid.ColumnDefinitions>
        <TextBlock Text='{Binding Glyph}' FontFamily='Segoe Fluent Icons, Segoe MDL2 Assets' FontSize='16' VerticalAlignment='Center' Foreground='{DynamicResource Accent}'/>
        <StackPanel Grid.Column='1' VerticalAlignment='Center' Margin='0,8,12,8'>
          <TextBlock Text='{Binding Title}' Style='{StaticResource Cell}' FontWeight='SemiBold'/>
          <TextBlock Text='{Binding App}' Style='{StaticResource CellSub}' FontSize='12' TextWrapping='Wrap' TextTrimming='None' Margin='0,1,0,0'/>
        </StackPanel>
        <TextBlock Grid.Column='2' Text='{Binding Status}' Style='{StaticResource CellSub}' Margin='0,0,8,0'/>
      </Grid>
    </DataTemplate>
    <DataTemplate x:Key='WinRow'>
      <Grid Height='36'>
        <Grid.ColumnDefinitions><ColumnDefinition Width='*' MinWidth='120'/><ColumnDefinition Width='{DynamicResource WColApp}'/><ColumnDefinition Width='{DynamicResource WColSize}'/><ColumnDefinition Width='{DynamicResource WColStatus}'/></Grid.ColumnDefinitions>
        <DockPanel Grid.Column='0' Margin='0,0,12,0'>
          <Grid DockPanel.Dock='Left' Width='28'>
            <Image Source='{Binding Icon}' Width='16' Height='16' HorizontalAlignment='Left' VerticalAlignment='Center' RenderOptions.BitmapScalingMode='HighQuality'/>
            <TextBlock Text='{Binding Glyph}' FontFamily='Segoe Fluent Icons, Segoe MDL2 Assets' FontSize='16' VerticalAlignment='Center' Foreground='{DynamicResource Sub}'/>
          </Grid>
          <Button DockPanel.Dock='Right' Tag='FreezeWarn' Visibility='{Binding FreezeVis}' ToolTip='The picture may freeze. Click for the fix.' Focusable='False'
                  Width='28' Height='26' Padding='0' Margin='6,0,0,0' Background='Transparent' BorderThickness='0' Cursor='Hand'>
            <TextBlock Text='&#xE7BA;' FontFamily='Segoe Fluent Icons, Segoe MDL2 Assets' FontSize='14' Foreground='{DynamicResource Accent}'/>
          </Button>
          <TextBlock Text='{Binding Title}' Style='{StaticResource Cell}'/>
        </DockPanel>
        <TextBlock Grid.Column='1' Text='{Binding App}' Style='{StaticResource CellSub}' Margin='0,0,8,0'/>
        <TextBlock Grid.Column='2' Text='{Binding SizeText}' Style='{StaticResource CellSub}'/>
        <TextBlock Grid.Column='3' Text='{Binding Status}' Style='{StaticResource CellSub}'/>
      </Grid>
    </DataTemplate>
    <DataTemplate x:Key='RunRow'>
      <Grid Height='36'>
        <Grid.ColumnDefinitions><ColumnDefinition Width='*' MinWidth='120'/><ColumnDefinition Width='{DynamicResource RColApp}'/><ColumnDefinition Width='{DynamicResource RColPos}'/><ColumnDefinition Width='{DynamicResource RColSize}'/><ColumnDefinition Width='{DynamicResource RColOpacity}'/><ColumnDefinition Width='{DynamicResource RColMode}'/><ColumnDefinition Width='40'/></Grid.ColumnDefinitions>
        <DockPanel Grid.Column='0' Margin='0,0,12,0'>
          <Grid DockPanel.Dock='Left' Width='28'>
            <Image Source='{Binding Icon}' Width='16' Height='16' HorizontalAlignment='Left' VerticalAlignment='Center' RenderOptions.BitmapScalingMode='HighQuality'/>
            <TextBlock Text='{Binding Glyph}' FontFamily='Segoe Fluent Icons, Segoe MDL2 Assets' FontSize='16' VerticalAlignment='Center' Foreground='{DynamicResource Sub}'/>
          </Grid>
          <TextBlock Text='{Binding Title}' Style='{StaticResource Cell}'/>
        </DockPanel>
        <TextBlock Grid.Column='1' Text='{Binding App}' Style='{StaticResource CellSub}' Margin='0,0,8,0'/>
        <TextBlock Grid.Column='2' Text='{Binding Position}' Style='{StaticResource CellSub}'/>
        <TextBlock Grid.Column='3' Text='{Binding SizeText}' Style='{StaticResource CellSub}'/>
        <TextBlock Grid.Column='4' Text='{Binding OpacityText}' Style='{StaticResource CellSub}'/>
        <TextBlock Grid.Column='5' Text='{Binding Mode}' Style='{StaticResource CellSub}'/>
        <ui:Button Grid.Column='6' Tag='RowMore' Appearance='Transparent' BorderThickness='0' Width='32' Height='28' Padding='0' HorizontalAlignment='Right' VerticalAlignment='Center' ToolTip='PiP options'>
          <TextBlock Text='&#xE712;' FontFamily='Segoe Fluent Icons, Segoe MDL2 Assets' FontSize='14'/>
        </ui:Button>
      </Grid>
    </DataTemplate>
  </ui:FluentWindow.Resources>

  <Grid>
    <Grid.RowDefinitions><RowDefinition Height='Auto'/><RowDefinition Height='*'/><RowDefinition Height='Auto'/></Grid.RowDefinitions>

    <!-- Title bar: the WPF-UI title bar supplies the caption buttons and dragging; the icon and name sit on top like Windows 11 apps -->
    <ui:TitleBar Grid.Row='0' Title=''/>
    <StackPanel Grid.Row='0' Orientation='Horizontal' VerticalAlignment='Center' IsHitTestVisible='False' Margin='16,0,0,0'>
      <Image x:Name='AppIcon' Width='16' Height='16' Margin='0,0,12,0' RenderOptions.BitmapScalingMode='HighQuality'/>
      <TextBlock Text='PiP Studio' FontSize='12' VerticalAlignment='Center' Foreground='{DynamicResource Text}'/>
    </StackPanel>

    <Grid Grid.Row='1' x:Name='BodyGrid'>
      <Grid.ColumnDefinitions><ColumnDefinition Width='Auto'/><ColumnDefinition Width='*'/></Grid.ColumnDefinitions>

      <!-- NavigationView pane -->
      <Border x:Name='NavSpace' Grid.Column='0' Width='200'/>
      <Border x:Name='Scrim' Grid.Column='0' Grid.ColumnSpan='2' Background='Transparent' Visibility='Collapsed' Panel.ZIndex='5'/>
      <Border x:Name='NavPane' Grid.Column='0' Grid.ColumnSpan='2' HorizontalAlignment='Left' Width='200' ClipToBounds='True' Panel.ZIndex='10'
              Background='Transparent' BorderThickness='0' CornerRadius='0,8,8,0'>
        <Grid x:Name='NavRoot'>
          <DockPanel Margin='0,0,0,6'>
            <Button x:Name='Burger' DockPanel.Dock='Top' Style='{StaticResource Subtle}' Width='40' Height='36' Margin='4,2,0,2' HorizontalAlignment='Left' ToolTip='Open or close navigation'>
              <TextBlock Text='&#xE700;' FontFamily='Segoe Fluent Icons, Segoe MDL2 Assets' FontSize='16'/>
            </Button>
            <Grid DockPanel.Dock='Top' Height='40' Margin='0,4,0,6'>
              <Grid x:Name='SearchWrap' Margin='8,0,8,0'>
                <TextBox x:Name='SearchBox' Height='34' Padding='11,6,34,6' VerticalContentAlignment='Center'/>
                <TextBlock x:Name='SearchHint' Text='Find a setting' Foreground='{DynamicResource Tertiary}' IsHitTestVisible='False' VerticalAlignment='Center' Margin='12,0,34,0' FontSize='14'/>
                <TextBlock Text='&#xE721;' FontFamily='Segoe Fluent Icons, Segoe MDL2 Assets' FontSize='12' Foreground='{DynamicResource Sub}' IsHitTestVisible='False' VerticalAlignment='Center' HorizontalAlignment='Right' Margin='0,0,12,0'/>
              </Grid>
              <Button x:Name='SearchIconBtn' Style='{StaticResource Subtle}' Width='40' Height='36' Margin='4,0,0,0' HorizontalAlignment='Left' Visibility='Collapsed' ToolTip='Search'>
                <TextBlock Text='&#xE721;' FontFamily='Segoe Fluent Icons, Segoe MDL2 Assets' FontSize='16'/>
              </Button>
            </Grid>
            <StackPanel DockPanel.Dock='Bottom'>
              <TextBlock x:Name='PadStatus' Style='{StaticResource Caption}' Margin='18,0,12,6' TextTrimming='CharacterEllipsis' TextWrapping='NoWrap'/>
              <RadioButton x:Name='Nav6' Tag='&#xE713;' Content='Settings' GroupName='nav' Style='{StaticResource Nav}'/>
            </StackPanel>
            <StackPanel>
              <RadioButton x:Name='Nav0' Tag='&#xE7C4;' Content='Windows' GroupName='nav' IsChecked='True' Style='{StaticResource Nav}'/>
              <RadioButton x:Name='Nav1' Tag='&#xE8A7;' Content='Running PiPs' GroupName='nav' Style='{StaticResource Nav}'/>
              <Border Height='1' Margin='16,8,16,8' Background='{DynamicResource Divider}'/>
              <RadioButton x:Name='Nav2' Tag='&#xE790;' Content='Appearance' GroupName='nav' Style='{StaticResource Nav}'/>
              <RadioButton x:Name='Nav3' Tag='&#xE8B0;' Content='Behavior' GroupName='nav' Style='{StaticResource Nav}'/>
              <RadioButton x:Name='Nav4' Tag='&#xE7FC;' Content='Controller' GroupName='nav' Style='{StaticResource Nav}'/>
              <RadioButton x:Name='Nav5' Tag='&#xE765;' Content='Shortcuts' GroupName='nav' Style='{StaticResource Nav}'/>
              <RadioButton x:Name='Nav7' Tag='&#xE8EA;' Content='Phone' GroupName='nav' Style='{StaticResource Nav}'/>
            </StackPanel>
          </DockPanel>
          <Canvas x:Name='IndCanvas' IsHitTestVisible='False'>
            <Rectangle x:Name='Indicator' Width='3' Height='16' RadiusX='1.5' RadiusY='1.5' Fill='{DynamicResource Accent}' Canvas.Left='4' Canvas.Top='0' Opacity='0' RenderTransformOrigin='0.5,0.5'>
              <Rectangle.RenderTransform><ScaleTransform ScaleY='1'/></Rectangle.RenderTransform>
            </Rectangle>
          </Canvas>
        </Grid>
      </Border>

      <!-- Content layer: lighter surface with a rounded top-left corner, like every Windows 11 NavigationView app -->
      <Border x:Name='ContentLayer' Grid.Column='1' Background='{DynamicResource Layer}' BorderBrush='{DynamicResource LayerBorder}' BorderThickness='1,1,0,0' CornerRadius='8,0,0,0'>
        <Grid>
          <Grid.RowDefinitions><RowDefinition Height='Auto'/><RowDefinition Height='Auto'/><RowDefinition Height='*'/></Grid.RowDefinitions>
          <!-- Controller mode: the navigation becomes tabs across the top, switched with LB / RB -->
          <Grid x:Name='PadTabs' Grid.Row='0' Visibility='Collapsed' Margin='24,12,24,0'>
            <Grid.ColumnDefinitions><ColumnDefinition Width='Auto'/><ColumnDefinition Width='*'/><ColumnDefinition Width='Auto'/></Grid.ColumnDefinitions>
            <Border x:Name='TabLB' Grid.Column='0' VerticalAlignment='Center'/>
            <Viewbox Grid.Column='1' StretchDirection='DownOnly' HorizontalAlignment='Center' Margin='12,0'>
              <Grid>
                <StackPanel x:Name='TabStrip' Orientation='Horizontal' Margin='0,0,0,8'/>
                <Canvas x:Name='TabCanvas' IsHitTestVisible='False'>
                  <Rectangle x:Name='TabBar' Height='3' RadiusX='1.5' RadiusY='1.5' Fill='{DynamicResource Accent}' Width='16' Opacity='0'/>
                </Canvas>
              </Grid>
            </Viewbox>
            <Border x:Name='TabRB' Grid.Column='2' VerticalAlignment='Center'/>
          </Grid>
          <!-- page title on its own line, the page's commands under it (they wrap when the window is narrow) -->
          <StackPanel x:Name='PageHeader' Grid.Row='1' Margin='36,22,28,10'>
            <TextBlock x:Name='PageTitle' Style='{StaticResource PageTitle}' Text='Windows' TextTrimming='CharacterEllipsis'/>
            <WrapPanel x:Name='CmdBar' Orientation='Horizontal' Margin='-11,8,0,0'/>
          </StackPanel>

          <!-- Settings pages: an animated explainer for the setting you're pointing at -->
          <Border x:Name='ExplainCard' Grid.Row='1' Grid.RowSpan='2' Width='316' Margin='0,22,28,24' HorizontalAlignment='Right' VerticalAlignment='Top' Visibility='Collapsed' Padding='16,14,16,16' CornerRadius='8' BorderThickness='1' Background='{DynamicResource Card}' BorderBrush='{DynamicResource CardBorder}'>
            <StackPanel>
              <Border Height='178' CornerRadius='4' Background='#FF15161A' ClipToBounds='True'>
                <Viewbox Stretch='Uniform' Margin='6'>
                  <Canvas x:Name='ExplainStage' Width='264' Height='150' ClipToBounds='True'/>
                </Viewbox>
              </Border>
              <TextBlock x:Name='ExplainTitle' Margin='0,14,0,0' FontSize='16' FontWeight='SemiBold' Foreground='{DynamicResource Text}' TextWrapping='Wrap'/>
              <TextBlock x:Name='ExplainText' Margin='0,4,0,0' Style='{StaticResource Caption}' FontSize='13' TextWrapping='Wrap'/>
              <DockPanel Margin='0,12,0,0'>
                <Button x:Name='ExplainHide' DockPanel.Dock='Right' Padding='10,4' Margin='8,0,0,0' Content='Hide' ToolTip='Hide the explanations (you can bring them back from the top of the page)'/>
                <TextBlock Style='{StaticResource Caption}' FontSize='11' Foreground='{DynamicResource Tertiary}' TextWrapping='Wrap' VerticalAlignment='Center' Text='Point at a setting to see what it does.'/>
              </DockPanel>
            </StackPanel>
          </Border>

          <!-- Windows page details: preview and actions, level with the page title -->
              <Border x:Name='DetailsCard' Grid.Row='1' Grid.RowSpan='2' Width='316' Margin='0,22,28,24' HorizontalAlignment='Right' VerticalAlignment='Top' Visibility='Collapsed' Padding='16,14,6,14' CornerRadius='8' BorderThickness='1' Background='{DynamicResource Card}' BorderBrush='{DynamicResource CardBorder}'>
                <!-- the preview stays put at the top; everything under it scrolls when the window is short -->
                <DockPanel>
                  <Border x:Name='PreviewBox' DockPanel.Dock='Top' Height='178' Margin='0,0,10,0' CornerRadius='4' Background='{DynamicResource Preview}'>
                    <TextBlock x:Name='PreviewNote' Text='Live preview' Style='{StaticResource Caption}' HorizontalAlignment='Center' VerticalAlignment='Center' TextAlignment='Center'/>
                  </Border>
                  <ScrollViewer x:Name='DetailsScroll' VerticalScrollBarVisibility='Auto' HorizontalScrollBarVisibility='Disabled' Padding='0,0,10,0'>
                  <StackPanel>
                  <TextBlock x:Name='SelTitle' FontSize='14' FontWeight='SemiBold' Foreground='{DynamicResource Text}' Margin='0,14,0,0' TextWrapping='Wrap' MaxHeight='40' TextTrimming='CharacterEllipsis'/>
                  <TextBlock x:Name='SelSub' Style='{StaticResource Caption}' Margin='0,2,0,12'/>
                  <ui:Button x:Name='OpenBtn' Appearance='Primary' HorizontalAlignment='Stretch' Height='32' Content='Open in picture-in-picture'/>
                  <Border x:Name='ChromiumCard' Visibility='Collapsed' Margin='0,14,0,0' Padding='14,12' CornerRadius='4' BorderThickness='1' BorderBrush='{DynamicResource CardBorder}' Background='{DynamicResource Attention}'>
                    <Grid>
                      <Grid.ColumnDefinitions><ColumnDefinition Width='Auto'/><ColumnDefinition Width='*'/></Grid.ColumnDefinitions>
                      <Grid Width='16' Height='16' VerticalAlignment='Top' Margin='0,2,12,0'>
                        <Ellipse Fill='{DynamicResource Accent}'/>
                        <TextBlock Text='&#xE946;' FontFamily='Segoe Fluent Icons, Segoe MDL2 Assets' FontSize='10' Foreground='{DynamicResource OnAccent}' HorizontalAlignment='Center' VerticalAlignment='Center'/>
                      </Grid>
                      <StackPanel Grid.Column='1'>
                        <TextBlock Text='The picture may freeze' FontWeight='SemiBold' FontSize='14' Foreground='{DynamicResource Text}'/>
                        <TextBlock Style='{StaticResource Caption}' FontSize='13' Margin='0,2,0,10' Text='This app stops drawing while another window covers it. Restart it with the fix to keep it moving.'/>
                        <Button x:Name='FixNow' Content='Restart with the fix' HorizontalAlignment='Left'/>
                        <ui:ToggleSwitch x:Name='FixAlways' Content='Always start it with the fix' Margin='0,10,0,0'/>
                        <TextBlock x:Name='FixState' Style='{StaticResource Caption}' Margin='0,6,0,0'/>
                      </StackPanel>
                    </Grid>
                  </Border>
                  </StackPanel>
                  </ScrollViewer>
                </DockPanel>
              </Border>

          <Grid Grid.Row='2'>
            <!-- Windows page (Task Manager table + details) -->
            <Grid x:Name='PageWindows' Margin='24,0,28,24'>
              <Grid.ColumnDefinitions><ColumnDefinition Width='*'/><ColumnDefinition Width='Auto'/></Grid.ColumnDefinitions>
              <DockPanel>
                <Grid DockPanel.Dock='Top' Width='320' HorizontalAlignment='Left' Margin='0,0,0,12'>
                  <TextBox x:Name='WinSearch' Height='34' Padding='11,6,34,6' VerticalContentAlignment='Center'/>
                  <TextBlock x:Name='WinSearchHint' Text='Search windows' Foreground='{DynamicResource Tertiary}' IsHitTestVisible='False' VerticalAlignment='Center' Margin='12,0,34,0' FontSize='14'/>
                  <TextBlock Text='&#xE721;' FontFamily='Segoe Fluent Icons, Segoe MDL2 Assets' FontSize='12' Foreground='{DynamicResource Sub}' IsHitTestVisible='False' VerticalAlignment='Center' HorizontalAlignment='Right' Margin='0,0,12,0'/>
                </Grid>
                <ListBox x:Name='AutoList' DockPanel.Dock='Top' Background='Transparent' BorderThickness='0' Padding='0' Margin='0,0,0,10' ItemTemplate='{StaticResource AutoRow}' ItemContainerStyle='{StaticResource RowItem}' HorizontalContentAlignment='Stretch' ScrollViewer.HorizontalScrollBarVisibility='Disabled' ScrollViewer.VerticalScrollBarVisibility='Disabled'/>
                <Grid x:Name='WinHeader' DockPanel.Dock='Top' Height='32' Margin='14,0,22,0'>
                  <Grid.ColumnDefinitions><ColumnDefinition Width='*' MinWidth='120'/><ColumnDefinition Width='{DynamicResource WColApp}'/><ColumnDefinition Width='{DynamicResource WColSize}'/><ColumnDefinition Width='{DynamicResource WColStatus}'/></Grid.ColumnDefinitions>
                </Grid>
                <Border DockPanel.Dock='Top' Height='1' Background='{DynamicResource Divider}' Margin='0,0,0,4'/>
                <ListBox x:Name='WinList' Background='Transparent' BorderThickness='0' Padding='0' ItemTemplate='{StaticResource WinRow}' ItemContainerStyle='{StaticResource RowItem}' HorizontalContentAlignment='Stretch' ScrollViewer.HorizontalScrollBarVisibility='Disabled' VirtualizingPanel.IsVirtualizing='True' VirtualizingPanel.ScrollUnit='Pixel'/>
              </DockPanel>
            </Grid>

            <!-- Running PiPs page -->
            <Grid x:Name='PageRunning' Margin='24,0,28,24' Visibility='Collapsed'>
              <DockPanel>
                <Grid x:Name='RunHeader' DockPanel.Dock='Top' Height='32' Margin='14,0,22,0'>
                  <Grid.ColumnDefinitions><ColumnDefinition Width='*' MinWidth='120'/><ColumnDefinition Width='{DynamicResource RColApp}'/><ColumnDefinition Width='{DynamicResource RColPos}'/><ColumnDefinition Width='{DynamicResource RColSize}'/><ColumnDefinition Width='{DynamicResource RColOpacity}'/><ColumnDefinition Width='{DynamicResource RColMode}'/><ColumnDefinition Width='40'/></Grid.ColumnDefinitions>
                </Grid>
                <Border DockPanel.Dock='Top' Height='1' Background='{DynamicResource Divider}' Margin='0,0,0,4'/>
                <!-- windows you marked with Treat as a PiP: below the running PiPs, so they're easy to find and undo -->
                <Border x:Name='MarkedBox' DockPanel.Dock='Bottom' Visibility='Collapsed' Margin='0,16,0,0'>
                  <StackPanel>
                    <Grid Margin='2,0,0,4'>
                      <TextBlock Text='Marked as PiPs' FontSize='16' FontWeight='SemiBold' Foreground='{DynamicResource Text}' VerticalAlignment='Center'/>
                      <Button x:Name='MarkedClear' Content='Remove all' HorizontalAlignment='Right' Padding='14,4'/>
                    </Grid>
                    <TextBlock Text='Windows you chose to treat as PiPs. Removing one makes it a normal window again and takes it off always on top.' FontSize='12' Foreground='{DynamicResource Sub}' TextWrapping='Wrap' Margin='2,0,0,8'/>
                    <ScrollViewer x:Name='MarkedScroll' MaxHeight='212' VerticalScrollBarVisibility='Auto' HorizontalScrollBarVisibility='Disabled'><StackPanel x:Name='MarkedList'/></ScrollViewer>
                  </StackPanel>
                </Border>
                <ListBox x:Name='RunList' Background='Transparent' BorderThickness='0' Padding='0' ItemTemplate='{StaticResource RunRow}' ItemContainerStyle='{StaticResource RowItem}' HorizontalContentAlignment='Stretch' ScrollViewer.HorizontalScrollBarVisibility='Disabled' VirtualizingPanel.ScrollUnit='Pixel'/>
              </DockPanel>
              <StackPanel x:Name='RunEmptyPanel' HorizontalAlignment='Center' VerticalAlignment='Center' Margin='20' Visibility='Collapsed'>
                <TextBlock Text='&#xE8A7;' FontFamily='Segoe Fluent Icons, Segoe MDL2 Assets' FontSize='40' Foreground='{DynamicResource Tertiary}' HorizontalAlignment='Center' Margin='0,0,0,12'/>
                <TextBlock x:Name='RunEmpty' FontSize='14' Foreground='{DynamicResource Sub}' TextAlignment='Center' TextWrapping='Wrap' MaxWidth='420'/>
              </StackPanel>
            </Grid>

            <!-- Settings pages -->
            <ScrollViewer x:Name='PageScroll' Visibility='Collapsed' VerticalScrollBarVisibility='Auto' HorizontalScrollBarVisibility='Disabled' CanContentScroll='False'>
              <StackPanel x:Name='SettingsHost' Margin='36,0,36,36' MaxWidth='1000' HorizontalAlignment='Stretch'/>
            </ScrollViewer>
          </Grid>
        </Grid>
      </Border>
    </Grid>

    <!-- Controller-friendly mode: a focus ring that glides between items, and a bar of button hints -->
    <Canvas x:Name='PadLayer' Grid.Row='1' IsHitTestVisible='False' Panel.ZIndex='50' ClipToBounds='True'>
      <Border x:Name='PadRing' BorderThickness='2.5' CornerRadius='7' BorderBrush='{DynamicResource Accent}' Opacity='0' Width='10' Height='10'>
        <Border.Effect><DropShadowEffect BlurRadius='14' ShadowDepth='0' Opacity='0.55' Color='Black'/></Border.Effect>
      </Border>
    </Canvas>
    <Border x:Name='PadBar' Grid.Row='2' Visibility='Collapsed' Height='48' Background='{DynamicResource Layer}' BorderBrush='{DynamicResource LayerBorder}' BorderThickness='0,1,0,0'>
      <StackPanel x:Name='PadHintsPanel' Orientation='Horizontal' HorizontalAlignment='Right' VerticalAlignment='Center' Margin='0,0,24,0'/>
    </Border>
  </Grid>
</ui:FluentWindow>";

        // ----------------------------------------------------------------------------- open / close
        public static void Open(string pageKey)
        {
            if (w == null) Build();
            if (!w.IsVisible)
            {
                // keep the window invisible (DWM cloak) until WPF has drawn its first frame, so it never flashes white
                var helper = new WindowInteropHelper(w);
                if (helper.Handle == IntPtr.Zero) helper.EnsureHandle();
                Cloak(helper.Handle, true);
                w.Show();
                UncloakAfterRender(helper.Handle);
            }
            if (w.WindowState == WindowState.Minimized) w.WindowState = WindowState.Normal;
            w.Activate();
            try { U.ForceForeground(new WindowInteropHelper(w).Handle); } catch { }
            if (pageKey != null) Navigate(pageKey);
        }

        static T F<T>(string name) where T : class { return (T)w.FindName(name); }

        /// <summary>Applies the language picked in Settings right away: rebuilds this window in place, same spot and size, back on the same page.</summary>
        static void SwitchLanguage()
        {
            L.Init(App.S.Language);
            App.I.SettingsChanged();
            var old = w;
            if (old == null) return;
            bool max = old.WindowState == WindowState.Maximized;
            var rb = old.RestoreBounds;
            string pg = page;
            exiting = true;                 // closing it must not quit or show the "still running" tip
            try { old.Close(); } catch { }
            exiting = false;
            Build();
            if (!rb.IsEmpty) { w.WindowStartupLocation = WindowStartupLocation.Manual; w.Left = rb.Left; w.Top = rb.Top; w.Width = rb.Width; w.Height = rb.Height; }
            if (max) w.WindowState = WindowState.Maximized;
            Open(pg == "search" ? "settings" : pg);
            Dispatch(() => L.Walk(w), DispatcherPriority.Loaded);
        }

        internal static void Cloak(IntPtr h, bool on)
        {
            if (h == IntPtr.Zero) return;
            int v = on ? 1 : 0;
            try { Native.DwmSetWindowAttribute(h, 13, ref v, 4); } catch { }   // DWMWA_CLOAK
        }

        internal static void UncloakAfterRender(IntPtr h)
        {
            int frames = 0;
            bool done = false;
            EventHandler tick = null;
            Action finish = () =>
            {
                if (done) return;
                done = true;
                CompositionTarget.Rendering -= tick;
                Cloak(h, false);
            };
            tick = (o, e) => { if (++frames >= 2) finish(); };              // two frames: the first one has reached the screen
            CompositionTarget.Rendering += tick;
            DelayRun(400, finish);                                           // never stay hidden
        }

        static void Build()
        {
            exiting = false;
            Fluent.PickBrushes();
            var win = (Window)XamlReader.Parse(Looks.Xaml(App.S.LiteMode ? LiteXaml(Xaml) : Xaml));
            w = win;
            Looks.Apply(win);
            L.Watch(win);
            ImageSource appIcon = null;
            if (App.AppIconHandle != IntPtr.Zero)
            {
                try
                {
                    var src = Imaging.CreateBitmapSourceFromHIcon(App.AppIconHandle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                    src.Freeze();
                    appIcon = src;
                    win.Icon = src;
                }
                catch { }
            }
            F<Image>("AppIcon").Source = appIcon;

            winList = F<ListBox>("WinList"); runList = F<ListBox>("RunList"); autoList = F<ListBox>("AutoList"); autoSig = ""; winFilter = ""; lastColsFor = -1;
            SmoothScroll.AttachInside(winList); SmoothScroll.AttachInside(runList);
            markedList = F<StackPanel>("MarkedList"); markedBox = F<Border>("MarkedBox"); markedSig = "";
            SmoothScroll.Attach(F<ScrollViewer>("MarkedScroll"));
            F<Button>("MarkedClear").Click += (o, e) => Ask("Remove every mark?",
                "Every window you marked with Treat as a PiP goes back to being a normal window, and comes off always on top.",
                "Remove all", "Cancel", ok =>
                {
                    if (!ok) return;
                    foreach (var m in ExternalPip.Marks()) ExternalPip.Unmark(m);
                    runSig = ""; winSig = ""; RefreshRunning(); RefreshMarked(); BuildCommands();
                });
            SmoothScroll.Attach(F<ScrollViewer>("DetailsScroll"));
            navPane = F<Border>("NavPane"); previewBox = F<Border>("PreviewBox"); chromiumCard = F<Border>("ChromiumCard"); detailsCard = F<Border>("DetailsCard"); pageHeader = F<StackPanel>("PageHeader");
            contentLayer = F<Border>("ContentLayer");
            pageTitle = F<TextBlock>("PageTitle"); searchHint = F<TextBlock>("SearchHint"); padStatus = F<TextBlock>("PadStatus");
            selTitle = F<TextBlock>("SelTitle"); selSub = F<TextBlock>("SelSub"); runEmpty = F<TextBlock>("RunEmpty"); fixState = F<TextBlock>("FixState");
            previewNote = F<TextBlock>("PreviewNote");
            searchBox = F<TextBox>("SearchBox"); searchWrap = F<Grid>("SearchWrap"); cmdBar = F<WrapPanel>("CmdBar");
            pageWindows = F<Grid>("PageWindows"); pageRunning = F<Grid>("PageRunning"); winHeader = F<Grid>("WinHeader"); runHeader = F<Grid>("RunHeader");
            navRoot = F<Grid>("NavRoot");
            pageScroll = F<ScrollViewer>("PageScroll"); settingsHost = F<StackPanel>("SettingsHost");
            SmoothScroll.Attach(pageScroll);
            burger = F<Button>("Burger"); searchIconBtn = F<Button>("SearchIconBtn");
            navSpace = F<Border>("NavSpace"); scrim = F<Border>("Scrim");
            overlayOpen = false; paneStyled = false; lastExpanded = true;
            indicator = F<Rectangle>("Indicator"); indCanvas = F<Canvas>("IndCanvas"); indTop = -1;
            fixAlways = F<Wpf.Ui.Controls.ToggleSwitch>("FixAlways"); fixNow = F<Button>("FixNow");
            navBtns = Enumerable.Range(0, navKeys.Length).Select(i => F<RadioButton>("Nav" + i)).ToArray();
            for (int i = 0; i < navBtns.Length; i++)
            {
                int idx = i;
                navBtns[i].Checked += (o, e) => { if (page != navKeys[idx]) Navigate(navKeys[idx]); };
                navBtns[i].Click += (o, e) => { if (overlayOpen) { overlayOpen = false; ApplyLayout(true); } };
                navBtns[i].Click += (o, e) => { if (searchBox.Text.Length > 0 && page != navKeys[idx]) searchBox.Text = ""; };
            }

            // tables
            BuildHeader(winHeader, new[] { "Name", "App", "Size", "Status" }, new[] { "name", "app", "size", "status" }, true);
            BuildHeader(runHeader, new[] { "Showing", "App", "Position", "Size", "Opacity", "Mode" }, null, false);
            lastColsFor = -1;
            // the Auto option sits above the table; only one of the two lists has a selection at a time
            winList.SelectionChanged += (o, e) =>
            {
                if (syncingSel) return;
                if (winList.SelectedItem != null && autoList.SelectedItem != null) { syncingSel = true; autoList.SelectedItem = null; syncingSel = false; }
                OnWinSelected();
            };
            autoList.SelectionChanged += (o, e) =>
            {
                if (syncingSel) return;
                if (autoList.SelectedItem != null && winList.SelectedItem != null) { syncingSel = true; winList.SelectedItem = null; syncingSel = false; }
                OnWinSelected();
            };
            winList.MouseDoubleClick += (o, e) => { if (winList.SelectedItem != null) OpenSelected(); };
            autoList.MouseDoubleClick += (o, e) => { if (autoList.SelectedItem != null) OpenSelected(); };
            winList.KeyDown += (o, e) => { if (e.Key == Key.Enter) { OpenSelected(); e.Handled = true; } };
            autoList.KeyDown += (o, e) => { if (e.Key == Key.Enter) { OpenSelected(); e.Handled = true; } };
            F<Wpf.Ui.Controls.Button>("OpenBtn").Click += (o, e) => OpenSelected();
            // the warning icon on a row opens the freeze fix dialog for that window
            winList.AddHandler(System.Windows.Controls.Primitives.ButtonBase.ClickEvent, new RoutedEventHandler((o, e) =>
            {
                var b = e.OriginalSource as FrameworkElement;
                if (b == null || !"FreezeWarn".Equals(b.Tag)) return;
                var it = b.DataContext as WinRowItem;
                if (it == null) return;
                winList.SelectedItem = it;
                ShowFreezeDialog(it);
                e.Handled = true;
            }));
            fixNow.Click += (o, e) =>
            {
                var it = SelItem();
                if (it == null || it.IsDisplay) return;
                FrozenFix.Relaunch(it.Hwnd);
                DelayRun(5000, () => { winSig = ""; RefreshWindows(); });
            };
            fixAlways.Click += (o, e) => ApplyAlwaysFix(fixAlways.IsChecked == true);
            runList.SelectionChanged += (o, e) => BuildCommands();
            // options for one PiP: the ⋯ button on its row, or a right-click on the row
            runList.AddHandler(System.Windows.Controls.Primitives.ButtonBase.ClickEvent, new RoutedEventHandler((o, e) =>
            {
                var b = e.OriginalSource as FrameworkElement;
                if (b == null || !"RowMore".Equals(b.Tag as string)) return;
                var row = b.DataContext as PipRowItem;
                if (row == null) return;
                runList.SelectedItem = row;
                ShowPipOptions(row);
                e.Handled = true;
            }));
            runList.PreviewMouseRightButtonUp += (o, e) =>
            {
                var c = ItemsControl.ContainerFromElement(runList, e.OriginalSource as DependencyObject) as ListBoxItem;
                var row = c == null ? null : c.DataContext as PipRowItem;
                if (row == null) return;
                runList.SelectedItem = row;
                ShowPipOptions(row);
                e.Handled = true;
            };
            runList.MouseDoubleClick += (o, e) => { var p = SelectedPip(); if (p != null) p.BringSourceToFront(); else { var x = SelectedExternal(); if (x != IntPtr.Zero) U.ForceForeground(x); } };

            // search
            searchBox.TextChanged += (o, e) => OnSearch();
            // the Windows page has its own search, just for windows
            winSearch = F<TextBox>("WinSearch"); winSearchHint = F<TextBlock>("WinSearchHint");
            winSearch.TextChanged += (o, e) =>
            {
                winSearchHint.Visibility = winSearch.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
                winFilter = winSearch.Text.Trim(); winSig = ""; RefreshWindows();
            };
            winSearch.KeyDown += (o, e) =>
            {
                if (e.Key == Key.Escape && winSearch.Text.Length > 0) { winSearch.Text = ""; e.Handled = true; }
                else if (e.Key == Key.Down || e.Key == Key.Enter)
                {
                    if (winList.Items.Count > 0)
                    {
                        winList.SelectedIndex = Math.Max(0, winList.SelectedIndex);
                        var c = winList.ItemContainerGenerator.ContainerFromIndex(winList.SelectedIndex) as ListBoxItem;
                        if (c != null) c.Focus();
                    }
                    e.Handled = true;
                }
            };
            searchBox.GotKeyboardFocus += (o, e) => UpdateSearchHint();
            searchBox.LostKeyboardFocus += (o, e) => UpdateSearchHint();
            searchBox.KeyDown += (o, e) =>
            {
                if (e.Key == Key.Escape) { searchBox.Text = ""; e.Handled = true; }
                else if (e.Key == Key.Enter && page == "search")
                {
                    Action act;
                    var first = pages["search"].Children.OfType<UIElement>().FirstOrDefault(x => activators.ContainsKey(x));
                    if (first != null && activators.TryGetValue(first, out act)) act();
                    e.Handled = true;
                }
            };
            searchIconBtn.Click += (o, e) => { OpenPane(); Dispatch(() => searchBox.Focus(), DispatcherPriority.Input); };
            scrim.MouseLeftButtonDown += (o, e) => { overlayOpen = false; ApplyLayout(true); e.Handled = true; };
            win.PreviewKeyDown += OnPreviewKeyDown;
            // the live preview is a second copy of the source for Windows to draw: only while this window is in use
            win.Deactivated += (o, e) => HideThumb();
            win.Activated += (o, e) => DelayRun(60, UpdatePreview);
            PadUiInit(win);
            ExplainInit();
            F<Button>("ExplainHide").Click += (o, e) => ToggleExplainers();
            padGlow = padRing.Effect;
            ApplyLite();

            // navigation pane
            burger.Click += (o, e) =>
            {
                if (IsWide()) { userCompact = !userCompact; overlayOpen = false; }   // wide window: the pane pushes the content
                else overlayOpen = !overlayOpen;                                       // narrow window: the pane opens over the content
                ApplyLayout(true);
            };
            win.SizeChanged += (o, e) => ApplyLayout(false);
            navRoot.SizeChanged += (o, e) => MoveIndicator(false);

            BuildSettingsPages();

            win.SourceInitialized += (o, e) =>
            {
                try { if (App.S.ThemeMode == 0) Wpf.Ui.Appearance.SystemThemeWatcher.Watch(win); } catch { }
                // while the user drags the window edge, hide the live preview: the DWM thumbnail can't keep pace with WPF's layout and shakes
                try
                {
                    var src = HwndSource.FromHwnd(new WindowInteropHelper(win).Handle);
                    if (src != null) src.AddHook((IntPtr hwnd, int msg, IntPtr wp, IntPtr lp, ref bool handled) =>
                    {
                        if (msg == 0x0231) { sizing = true; HideThumb(); }                     // WM_ENTERSIZEMOVE
                        else if (msg == 0x0232) { sizing = false; DelayRun(60, UpdatePreview); } // WM_EXITSIZEMOVE
                        return IntPtr.Zero;
                    });
                }
                catch { }
            };
            win.Loaded += (o, e) => { Dispatch(() => MoveIndicator(false), DispatcherPriority.Loaded); AnimatePage(); Dispatch(() => L.Walk(w), DispatcherPriority.Loaded); };
            win.Closing += (o, e) =>
            {
                if (!exiting && !App.S.CloseToTray)
                {
                    exiting = true;
                    win.Dispatcher.BeginInvoke(new Action(App.I.ExitApp));
                    return;
                }
                if (!exiting && !closedHintShown && App.S.ShowTips)
                {
                    closedHintShown = true;
                    App.I.Balloon("PiP Studio is still running", "Your shortcuts keep working. Click the tray icon to open it again.");
                }
            };
            win.Closed += (o, e) => OnClosed(win);

            App.I.StateChanged += OnEngineState;
            tick = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
            tick.Tick += (o, e) => OnTick();
            tick.Start();

            var wa = SystemParameters.WorkArea;
            win.Width = Math.Min(1120, wa.Width - 40);
            win.Height = Math.Min(740, wa.Height - 40);
            Navigate(page == "search" ? "windows" : page, false);
            ApplyLayout(false);
            UpdateSearchHint();
        }

        static void OnClosed(Window win)
        {
            try { var m = typeof(Wpf.Ui.Appearance.SystemThemeWatcher).GetMethod("UnWatch"); if (m != null) m.Invoke(null, new object[] { win }); } catch { }
            if (w != win) return;
            if (tick != null) { tick.Stop(); tick = null; }
            if (App.I != null) App.I.StateChanged -= OnEngineState;
            HideThumb();
            if (captureKb != null) EndKeyCapture();
            if (App.I != null) App.I.CancelPadCapture();
            w = null; winList = runList = autoList = null; markedList = null; markedBox = null; navPane = previewBox = chromiumCard = detailsCard = contentLayer = null; explainCard = null; explainStage = null; if (explainSb != null) { explainSb = null; } pageHeader = null;
            pageTitle = searchHint = padStatus = selTitle = selSub = runEmpty = fixState = previewNote = null; searchBox = null; winSearch = null; winSearchHint = null; searchWrap = null; cmdBar = null;
            pageWindows = pageRunning = winHeader = runHeader = navRoot = null; pageScroll = null; settingsHost = null; navBtns = null;
            burger = searchIconBtn = null; indicator = null; indCanvas = null; navSpace = scrim = null; widthTokens.Clear();
            fixAlways = null; fixNow = null; refreshBindings = null; refreshUpdates = null; refreshCompanion = null;
            pages.Clear(); searchIndex.Clear(); iconCache.Clear(); winSig = runSig = "";
            if (!exiting && App.S.MinimizeMemory) App.RunSoon(U.Trim, 1500);
        }

        static void Dispatch(Action a, DispatcherPriority p = DispatcherPriority.Background)
        {
            if (w != null) w.Dispatcher.BeginInvoke(a, p);
        }

        static void DelayRun(int ms, Action a)
        {
            var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ms) };
            t.Tick += (o, e) => { t.Stop(); try { a(); } catch (Exception ex) { App.ReportError(ex); } };
            t.Start();
        }

        // ----------------------------------------------------------------------------- navigation pane
        const double PaneOpen = 200, PaneCompact = 48;   // open menu: just wide enough for the longest item and the search box

        static bool IsWide()
        {
            double wd = (w.ActualWidth > 0 ? w.ActualWidth : w.Width) / uiScale;   // controller mode zooms the layout
            // NavigationView expands at 1008 epx. Pages with a panel on the right (the Windows page preview, the settings
            // explanations) need much more room, so there the menu folds to icons earlier and the page keeps its space.
            if (PageHasSidePanel() || (App.S.ShowExplainers && (page == "windows" || page == "running"))) return wd >= PaneOpen + ContentChrome + SidePanelPageMin + 28 + 316 + 16;
            return wd >= PaneOpen + ContentChrome + DetailsMin;   // Windows page and every other page fold at the same width (~1010)
        }

        const double SidePanelPageMin = 700;   // settings/table width wanted next to the right-hand panel

        static bool PageHasSidePanel()
        {
            return App.S.ShowExplainers && ExplainPages.Contains(page ?? "");      // settings with explanations (not the Windows page)
        }

        static void OpenPane()
        {
            if (IsWide()) userCompact = false; else overlayOpen = true;
            ApplyLayout(true);
        }

        /// <summary>Animates a width; a newer animation on the same element cancels the older one's finish.</summary>
        static void AnimateWidth(FrameworkElement el, double target, bool animate, Action done = null)
        {
            int token;
            widthTokens.TryGetValue(el, out token);
            token++;
            widthTokens[el] = token;
            double cur = el.ActualWidth > 0 ? el.ActualWidth : el.Width;
            if (!animate || !Anim.On || Math.Abs(cur - target) < 0.5)
            {
                el.BeginAnimation(FrameworkElement.WidthProperty, null);
                el.Width = target;
                if (done != null) done();
                return;
            }
            bool opening = target > cur;
            var an = new DoubleAnimation(cur, target, TimeSpan.FromMilliseconds(opening ? 300 : 200)) { EasingFunction = opening ? Anim.Quint : Anim.Smooth };
            an.Completed += (o, e) =>
            {
                int now;
                if (!widthTokens.TryGetValue(el, out now) || now != token) return;
                el.BeginAnimation(FrameworkElement.WidthProperty, null);
                el.Width = target;
                if (done != null) done();
            };
            el.BeginAnimation(FrameworkElement.WidthProperty, an);
        }

        /// <summary>The floating pane gets a solid background, an edge and a shadow; inline it sits straight on Mica.</summary>
        static void StylePane(bool floating)
        {
            if (navPane == null || paneStyled == floating) return;
            paneStyled = floating;
            if (floating)
            {
                navPane.SetResourceReference(Border.BackgroundProperty, "PaneOverlay");
                navPane.SetResourceReference(Border.BorderBrushProperty, "CardBorder");
                navPane.BorderThickness = new Thickness(0, 1, 1, 1);
                if (!App.S.LiteMode) navPane.Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 28, ShadowDepth = 6, Direction = 315, Opacity = Looks.Dark ? 0.45 : 0.18, Color = Colors.Black };
            }
            else
            {
                navPane.SetResourceReference(Border.BackgroundProperty, "LookNav");   // transparent, or the Xbox look's pane
                navPane.BorderThickness = new Thickness(0);
                navPane.Effect = null;
            }
        }

        static void ApplyLayout(bool animate)
        {
            if (w == null || navPane == null) return;
            double wd = (w.ActualWidth > 0 ? w.ActualWidth : w.Width) / uiScale;   // controller mode zooms the layout
            if (padVisual)
            {
                // controller mode: no side pane, the pages are tabs across the top
                overlayOpen = false;
                navPane.Visibility = Visibility.Collapsed;
                scrim.Visibility = Visibility.Collapsed;
                navSpace.BeginAnimation(FrameworkElement.WidthProperty, null);
                navSpace.Width = 0;
                contentLayer.CornerRadius = Looks.CR(0);
                contentLayer.BorderThickness = new Thickness(0, 1, 0, 0);
                UpdateColumns(wd - ContentChrome);
                return;
            }
            if (navPane.Visibility != Visibility.Visible)
            {
                navPane.Visibility = Visibility.Visible;
                contentLayer.CornerRadius = Looks.CR(8, 0, 0, 0);
                contentLayer.BorderThickness = new Thickness(1, 1, 0, 0);
            }
            bool wide = IsWide();
            if (wide && overlayOpen) overlayOpen = false;           // grew wide: the pane goes back inline
            bool expanded = wide ? !userCompact : overlayOpen;
            bool floating = !wide && overlayOpen;
            compact = !expanded;

            double paneW = expanded ? PaneOpen : PaneCompact;
            double spaceW = wide ? paneW : PaneCompact;
            if (floating) StylePane(true);
            AnimateWidth(navPane, paneW, animate, () => { if (!overlayOpen) StylePane(false); });
            AnimateWidth(navSpace, spaceW, animate);
            scrim.Visibility = floating ? Visibility.Visible : Visibility.Collapsed;

            if (expanded != lastExpanded || !animate)
            {
                lastExpanded = expanded;
                searchWrap.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
                searchIconBtn.Visibility = expanded ? Visibility.Collapsed : Visibility.Visible;
                for (int i = 0; i < navBtns.Length; i++) navBtns[i].ToolTip = expanded ? null : navLabels[i];
                if (expanded) Anim.Fade(padStatus, 1, 200, 120); else Anim.Fade(padStatus, 0, 100);
            }
            UpdateColumns(wd - spaceW - ContentChrome);
        }

        static double lastColsFor = -1;
        const double DetailsMin = 760, ContentChrome = 52;   // content width needed for table + preview; page margins

        /// <summary>Responsive tables: drop the least important columns (and the details card) as the window narrows, so Name never gets squeezed out.</summary>
        static void UpdateColumns(double content)
        {
            if (w == null || detailsCard == null) return;
            content = Math.Round(content);
            if (Math.Abs(content - lastColsFor) < 1) return;
            lastColsFor = content;
            const double nameMin = 200, chrome = 36 + 28 + 8;   // header margins, icon, padding

            bool details = content >= DetailsMin;
            var dv = details ? Visibility.Visible : Visibility.Collapsed;
            detailsWanted = details;
            LayoutDetails();

            double avail = content - (details ? 332 : 0) - chrome - nameMin;
            double app = avail >= 150 ? 150 : 0; avail -= app;
            double status = avail >= 110 ? 110 : 0; avail -= status;
            double size = avail >= 110 ? 110 : 0;
            SetCol("WColApp", app); SetCol("WColStatus", status); SetCol("WColSize", size);
            ShowHeaderCells(winHeader, new[] { 1.0, app, size, status });

            avail = content - chrome - nameMin;
            double mode = avail >= 170 ? 170 : 0; avail -= mode;
            double rApp = avail >= 130 ? 130 : 0; avail -= rApp;
            double pos = avail >= 110 ? 110 : 0; avail -= pos;
            double rSize = avail >= 100 ? 100 : 0; avail -= rSize;
            double op = avail >= 70 ? 70 : 0;
            SetCol("RColMode", mode); SetCol("RColApp", rApp); SetCol("RColPos", pos); SetCol("RColSize", rSize); SetCol("RColOpacity", op);
            ShowHeaderCells(runHeader, new[] { 1.0, rApp, pos, rSize, op, mode });
        }

        /// <summary>The details card sits at the top right, next to the page title, on the Windows page only. The title, buttons and table make room for it.</summary>
        static void LayoutDetails()
        {
            if (detailsCard == null || pageHeader == null || pageWindows == null) return;
            bool show = detailsWanted && page == "windows";
            var dv = show ? Visibility.Visible : Visibility.Collapsed;
            if (detailsCard.Visibility != dv) { detailsCard.Visibility = dv; if (!show) HideThumb(); }
            double room = show ? 28 + 316 + 16 : 28;
            var hm = pageHeader.Margin;
            if (Math.Abs(hm.Right - room) > 0.5) pageHeader.Margin = new Thickness(hm.Left, hm.Top, room, hm.Bottom);
            var pm = pageWindows.Margin;
            if (Math.Abs(pm.Right - room) > 0.5) pageWindows.Margin = new Thickness(pm.Left, pm.Top, room, pm.Bottom);

            // settings pages: the explainer panel takes the same spot
            if (explainCard == null || pageScroll == null) return;
            bool ex = detailsWanted && ExplainWanted();
            var ev = ex ? Visibility.Visible : Visibility.Collapsed;
            if (explainCard.Visibility != ev)
            {
                explainCard.Visibility = ev;
                if (ex) { Anim.Enter(explainCard, 28, 60); ExplainForPage(); }
                else if (explainSb != null) explainSb.Stop(explainStage);   // nothing plays while it's hidden
            }
            double sroom = ex ? 28 + 316 + 12 : 0;   // clear of the panel (and its right margin), so the scrollbar sits beside it
            if (ex)
            {
                var hm2 = pageHeader.Margin;
                if (Math.Abs(hm2.Right - (sroom + 4)) > 0.5) pageHeader.Margin = new Thickness(hm2.Left, hm2.Top, sroom + 4, hm2.Bottom);
            }
            var sm = pageScroll.Margin;
            if (Math.Abs(sm.Right - sroom) > 0.5) pageScroll.Margin = new Thickness(sm.Left, sm.Top, sroom, sm.Bottom);
        }

        static void ShowHeaderCells(Grid header, double[] widths)
        {
            if (header == null) return;
            foreach (UIElement c in header.Children)
            {
                int col = Grid.GetColumn(c);
                var v = col < widths.Length && widths[col] <= 0 ? Visibility.Collapsed : Visibility.Visible;
                if (c.Visibility != v) c.Visibility = v;
            }
        }

        static void SetCol(string key, double px)
        {
            var cur = w.Resources[key] as GridLength?;
            if (cur.HasValue && Math.Abs(cur.Value.Value - px) < 0.5) return;
            w.Resources[key] = new GridLength(px);
        }

        /// <summary>Slides the accent pill to the selected item, stretching on the way like NavigationView.</summary>
        static void MoveIndicator(bool animate) { MoveIndicator(animate, 0); }

        static void MoveIndicator(bool animate, int attempt)
        {
            if (indicator == null || navBtns == null) return;
            int ni = Array.IndexOf(navKeys, page);
            if (ni < 0) { Anim.Fade(indicator, 0, 120); indTop = -1; return; }
            var item = navBtns[ni];
            if (item.ActualHeight <= 0) { if (attempt < 20) Dispatch(() => MoveIndicator(animate, attempt + 1), DispatcherPriority.Loaded); return; }
            double y;
            try { y = item.TranslatePoint(new Point(0, item.ActualHeight / 2), indCanvas).Y - 8; } catch { return; }
            var scale = (ScaleTransform)indicator.RenderTransform;
            if (!animate || indTop < 0 || !Anim.On)
            {
                indicator.BeginAnimation(Canvas.TopProperty, null);
                Canvas.SetTop(indicator, y);
                indTop = y;
                if (indicator.Opacity < 1) Anim.Fade(indicator, 1, animate ? 167 : 0);
                return;
            }
            double dist = Math.Abs(y - indTop);
            indicator.BeginAnimation(Canvas.TopProperty, new DoubleAnimation(indTop, y, TimeSpan.FromMilliseconds(320)) { EasingFunction = Anim.Smooth });
            var stretch = new DoubleAnimationUsingKeyFrames();
            stretch.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            stretch.KeyFrames.Add(new EasingDoubleKeyFrame(1 + Math.Min(2.2, dist / 40.0), KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(160)), Anim.Smooth));
            stretch.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(320)), Anim.Smooth));
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, stretch);
            Anim.Fade(indicator, 1, 83);
            indTop = y;
        }

        public static void Navigate(string key) { Navigate(key, true); }

        static void Navigate(string key, bool animate)
        {
            if (w == null) return;
            if (key == "pip") key = "windows";
            bool changed = key != page;
            if (L.Active) Dispatch(() => L.Walk(w), DispatcherPriority.Loaded);   // backup for the translation hook
            page = key;
            int ni = Array.IndexOf(navKeys, key);
            if (ni >= 0 && navBtns[ni].IsChecked != true) navBtns[ni].IsChecked = true;
            if (ni < 0) foreach (var nb in navBtns) nb.IsChecked = false;
            pageTitle.Text = ni >= 0 ? navLabels[ni] : "Search results";
            pageWindows.Visibility = key == "windows" ? Visibility.Visible : Visibility.Collapsed;
            pageRunning.Visibility = key == "running" ? Visibility.Visible : Visibility.Collapsed;
            bool settingsPage = key != "windows" && key != "running";
            pageScroll.Visibility = settingsPage ? Visibility.Visible : Visibility.Collapsed;
            foreach (var kv in pages) kv.Value.Visibility = kv.Key == key ? Visibility.Visible : Visibility.Collapsed;
            if (settingsPage && key != "search") { SmoothScroll.Jump(pageScroll, 0); lastSettingsPage = key; }
            if (key != "search" && searchBox.Text.Length > 0) searchBox.Text = "";   // leaving the settings results
            UpdateSearchHint();
            bool explainWasUp = explainCard != null && explainCard.Visibility == Visibility.Visible;
            if (changed) ApplyLayout(animate);                 // the menu's width depends on whether this page has a right-hand panel
            LayoutDetails();
            if (explainWasUp && explainCard.Visibility == Visibility.Visible && changed) ExplainForPage();   // new page: its first explainer
            BuildCommands();
            if (key == "windows") { winSig = ""; RefreshWindows(); }
            if (key == "running") { runSig = ""; RefreshRunning(); }
            if (key == "controller" && refreshBindings != null) refreshBindings();
            MoveIndicator(animate && changed);
            UpdateTabs(animate && changed);
            if (padVisual && changed) BuildPadHints();
            if (animate && changed) AnimatePage();
            UpdatePadStatus();
        }

        /// <summary>Page entrance: the title and command bar fade, the content rises; settings cards arrive one after another.</summary>
        static void AnimatePage()
        {
            if (w == null || !Anim.On) return;
            HideThumb();
            Anim.Enter(pageTitle, 10, 0, 250);
            Anim.Enter(cmdBar, 10, 30, 250);
            if (page == "windows") { Anim.Enter(pageWindows, 28, 40); if (detailsCard.Visibility == Visibility.Visible) Anim.Enter(detailsCard, 28, 60); }
            else if (page == "running") Anim.Enter(pageRunning, 28, 40);
            else
            {
                StackPanel sp;
                if (!pages.TryGetValue(page, out sp)) return;
                int i = 0;
                foreach (UIElement child in sp.Children)
                {
                    if (i > 16) { child.Opacity = 1; child.RenderTransform = null; continue; }
                    Anim.Enter(child, 20, 30 + i * 22, 340);
                    i++;
                }
            }
        }

        static void OnEngineState()
        {
            if (w == null) return;
            UpdatePadStatus();
            if (page == "running") { runSig = ""; RefreshRunning(); BuildCommands(); }
            if (page == "controller" && refreshBindings != null) refreshBindings();
        }

        static void UpdatePadStatus()
        {
            if (padStatus == null) return;
            var a = App.I;
            padStatus.Text = !App.S.PadEnabled ? "" : !a.PadConnected ? "No controller" : Pad.FamilyName(a.PadKind) + (a.PadOn ? " · controls on" : " · controls off");
        }

        static void UpdateSearchHint()
        {
            if (searchHint == null) return;
            searchHint.Text = "Find a setting";
            searchHint.Visibility = searchBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        static void OnTick()
        {
            if (w == null) return;
            tickCount++;
            if (tickCount % 6 == 0) UpdatePreview();                                  // ~10 times a second
            if (thumb != IntPtr.Zero && thumbAlpha < 255) FadeThumbIn();
            if (tickCount % 120 == 0 && page == "windows") RefreshWindows();          // every 2 s
            if (tickCount % 60 == 0 && page == "running") RefreshRunning();           // every second
        }

        // ----------------------------------------------------------------------------- Task Manager table headers
        static void BuildHeader(Grid header, string[] titles, string[] keys, bool sortable)
        {
            header.Children.Clear();
            for (int i = 0; i < titles.Length; i++)
            {
                var cell = new Grid { Background = Brushes.Transparent, Margin = new Thickness(i == 0 ? -8 : -8, 0, 4, 0) };
                var hov = new Border { CornerRadius = Looks.CR(4), Margin = new Thickness(0, 2, 0, 2) };
                hov.SetResourceReference(Border.BackgroundProperty, "Hover");
                var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
                if (i == 0) sp.Margin = new Thickness(8 + 28, 0, 8, 0);
                var tb = Fluent.Label(titles[i], 12, "Sub", false);
                var chev = Fluent.Glyph("", 8, "Sub");
                chev.Margin = new Thickness(6, 1, 0, 0);
                sp.Children.Add(tb);
                sp.Children.Add(chev);
                cell.Children.Add(hov);
                cell.Children.Add(sp);
                Grid.SetColumn(cell, i);
                header.Children.Add(cell);
                hov.Opacity = 0;
                if (!sortable) continue;
                cell.Cursor = Cursors.Hand;
                Anim.Hover(cell, hov);
                string k = keys[i];
                cell.MouseLeftButtonUp += (o, e) =>
                {
                    if (sortKey == k) sortDesc = !sortDesc; else { sortKey = k; sortDesc = false; }
                    foreach (var c in header.Children.OfType<Grid>()) ((TextBlock)((StackPanel)c.Children[1]).Children[1]).Text = "";
                    chev.Text = sortDesc ? "" : "";
                    winSig = "";
                    RefreshWindows();
                };
            }
        }

        // ----------------------------------------------------------------------------- command bar (Task Manager style)
        static Wpf.Ui.Controls.Button Cmd(string glyph, string text, Action click, bool enabled = true)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            var g = new TextBlock { Text = glyph, FontFamily = Fluent.IconFont, FontSize = 16, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
            sp.Children.Add(g);
            sp.Children.Add(new TextBlock { Text = text, FontSize = 14, VerticalAlignment = VerticalAlignment.Center });
            var b = new Wpf.Ui.Controls.Button
            {
                Content = sp,
                Height = 36,
                Margin = new Thickness(0, 0, 4, 2),
                Padding = new Thickness(11, 4, 11, 4),
                Appearance = Wpf.Ui.Controls.ControlAppearance.Transparent,
                BorderThickness = new Thickness(0),
                IsEnabled = enabled
            };
            b.Click += (o, e) => { try { click(); } catch (Exception ex) { App.ReportError(ex); } };
            return b;
        }

        static void BuildCommands()
        {
            if (cmdBar == null) return;
            cmdBar.Children.Clear();
            if (ExplainPages.Contains(page ?? ""))
            {
                // the explanations panel on the right: hide it, or bring it back
                cmdBar.Children.Add(Cmd(App.S.ShowExplainers ? "\uE7B3" : "\uE890", App.S.ShowExplainers ? "Hide explanations" : "Show explanations", ToggleExplainers));
            }
            if (page == "windows")
            {
                var it = SelItem();
                cmdBar.Children.Add(Cmd("", "Open in PiP", OpenSelected, it != null));
                cmdBar.Children.Add(Cmd("", "Click a window", () => App.I.PickOnScreen(null)));
                cmdBar.Children.Add(Cmd("", "Screen region", () => App.I.PipScreenRegion(null)));
                cmdBar.Children.Add(Cmd("", "Refresh", () => { U.ClearProcessCache(); iconCache.Clear(); winSig = ""; RefreshWindows(); }));
                bool real = it != null && !it.IsDisplay && !it.IsAuto && it.Hwnd != IntPtr.Zero;
                cmdBar.Children.Add(Cmd("\uE8A7", "Treat as a PiP", () =>
                {
                    var s2 = SelItem();
                    if (s2 == null || s2.IsDisplay || s2.IsAuto || s2.Hwnd == IntPtr.Zero) return;
                    IntPtr mh = s2.Hwnd;
                    string app = ExternalPip.AppName(mh);
                    // say what it means first: it changes how the window behaves (it stays on top, snaps, follows your shortcuts)
                    string which = ExternalPip.MarksWholeKind(mh)
                        ? "This applies to every window like this one from " + app + ", now and later."
                        : "This applies to this window only, until it closes.";
                    Ask("Treat this window as a PiP?",
                        "PiP Studio will handle it like another app's picture-in-picture window:\n\n" +
                        "•  It moves to Running PiPs and snaps to PiP spots\n" +
                        "•  It stays on top of your other windows once you move or steer it\n" +
                        "•  Your PiP shortcuts and controller move, resize, fade and close it\n\n" +
                        which + " You can undo it any time under Running PiPs > Marked as PiPs, which also takes it off always on top.",
                        "Treat as a PiP", "Cancel", ok =>
                        {
                            if (!ok || !Native.IsWindow(mh)) return;
                            ExternalPip.MarkPip(mh);                        // it moves to Running PiPs, where PiP Studio can steer it
                            winSig = ""; runSig = ""; RefreshWindows(); BuildCommands();
                        });
                }, real));
            }
            else if (page == "running" && SelectedExternal() != IntPtr.Zero && Native.IsIconic(SelectedExternal()))
            {
                // another app's PiP that's minimized: bring it back, or stop treating it as a PiP
                Func<IntPtr> x = SelectedExternal;
                cmdBar.Children.Add(Cmd("\uE923", "Restore", () => { ExternalPip.Restore(x()); DelayRun(250, () => { runSig = ""; RefreshRunning(); BuildCommands(); }); }, true));
                cmdBar.Children.Add(Cmd("\uE711", "Close", () => { ExternalPip.Handle("close", x()); DelayRun(300, () => { runSig = ""; RefreshRunning(); BuildCommands(); }); }, true));
                cmdBar.Children.Add(Cmd("\uE894", "Not a PiP", () => { ExternalPip.MarkNotPip(x()); runSig = ""; RefreshRunning(); RefreshMarked(); BuildCommands(); }, true));
            }
            else if (page == "running" && SelectedExternal() != IntPtr.Zero)
            {
                // another app's PiP: what PiP Studio can do with it
                Func<IntPtr> x = SelectedExternal;
                cmdBar.Children.Add(Cmd("\uE8B0", "Next corner", () => { ExternalPip.Handle("corner", x()); runSig = ""; RefreshRunning(); }, true));
                cmdBar.Children.Add(Cmd("\uE740", "Bigger", () => { ExternalPip.Handle("grow", x()); runSig = ""; RefreshRunning(); }, true));
                cmdBar.Children.Add(Cmd("\uE73F", "Smaller", () => { ExternalPip.Handle("shrink", x()); runSig = ""; RefreshRunning(); }, true));
                cmdBar.Children.Add(Cmd("\uE767", "Mute", () => ExternalPip.Handle("mute", x()), true));
                cmdBar.Children.Add(Cmd("\uE711", "Close", () => { ExternalPip.Handle("close", x()); DelayRun(300, () => { runSig = ""; RefreshRunning(); BuildCommands(); }); }, true));
                cmdBar.Children.Add(Cmd("\uE712", "Options", () => { var r = runList.SelectedItem as PipRowItem; if (r != null) ShowPipOptions(r); }, true));
                cmdBar.Children.Add(Cmd("\uE894", "Not a PiP", () => { ExternalPip.MarkNotPip(x()); runSig = ""; RefreshRunning(); RefreshMarked(); BuildCommands(); }, true));
            }
            else if (page == "running")
            {
                var p = SelectedPip();
                bool any = App.I.Pips.Any(x => !x.IsClosing), sel = p != null;
                bool win = sel && p.St.Kind == SourceKind.Window;
                cmdBar.Children.Add(Cmd("", "Change source", () => { var q = SelectedPip(); if (q != null) App.I.ShowPicker(q); }, sel));
                cmdBar.Children.Add(Cmd("", "Crop", () => { var q = SelectedPip(); if (q != null) q.StartCrop(); }, sel));
                cmdBar.Children.Add(Cmd("", "Show source", () => { var q = SelectedPip(); if (q != null) q.BringSourceToFront(); }, win));
                cmdBar.Children.Add(Cmd(win && p.St.Follow ? "" : "", "Follow", () => { var q = SelectedPip(); if (q != null) { q.SetFollow(!q.St.Follow); runSig = ""; RefreshRunning(); BuildCommands(); } }, win));
                cmdBar.Children.Add(Cmd(App.I.HiddenAll ? "" : "", App.I.HiddenAll ? "Show all" : "Hide all", () => { App.I.ToggleHideAll(); BuildCommands(); }, any));
                cmdBar.Children.Add(Cmd("\uE712", "Options", () => { var r = runList.SelectedItem as PipRowItem; if (r != null && r.Pip != null) ShowPipOptions(r); }, sel));
                cmdBar.Children.Add(Cmd("", "Close", () => { var q = SelectedPip(); if (q != null) q.CloseAnimated(); }, sel));
            }
            cmdBar.Visibility = cmdBar.Children.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        // ----------------------------------------------------------------------------- windows page
        static ImageSource IconFor(IntPtr hwnd)
        {
            ImageSource src;
            if (iconCache.TryGetValue(hwnd, out src)) return src;
            src = null;
            IntPtr hi = U.WindowIconHandle(hwnd, true);
            if (hi != IntPtr.Zero)
            {
                try
                {
                    var bs = Imaging.CreateBitmapSourceFromHIcon(hi, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                    bs.Freeze();
                    src = bs;
                }
                catch { }
            }
            iconCache[hwnd] = src;
            return src;
        }

        static void RefreshWindows()
        {
            if (winList == null) return;
            var items = new List<WinRowItem>();
            var used = new HashSet<IntPtr>(App.I.Pips.Where(p => !p.IsClosing && p.St.Kind == SourceKind.Window).Select(p => p.Src));
            int order = 0;
            foreach (var h in WinEnum.AltTab())
            {
                var r = U.WindowRect(h);
                bool min = Native.IsIconic(h);
                items.Add(new WinRowItem
                {
                    Hwnd = h,
                    Title = U.WindowTitle(h),
                    App = U.ProcessName(h),
                    SizeText = min ? "—" : r.Width + " × " + r.Height,
                    Status = used.Contains(h) ? "In PiP" : min ? "Minimized" : "",
                    Order = order++
                });
            }
            var screens = System.Windows.Forms.Screen.AllScreens;
            for (int i = 0; i < screens.Length; i++)
            {
                var b = screens[i].Bounds;
                items.Add(new WinRowItem
                {
                    IsDisplay = true,
                    ScreenBounds = b,
                    Title = "Display " + (i + 1) + (screens[i].Primary ? " (main)" : ""),
                    App = "Whole screen",
                    SizeText = b.Width + " × " + b.Height,
                    Status = App.I.Pips.Any(p => !p.IsClosing && p.St.Kind == SourceKind.Screen && p.St.ScreenRect == b) ? "In PiP" : "",
                    Glyph = "",
                    Order = 10000 + i
                });
            }
            string q = winFilter.Trim();
            if (q.Length > 0)
                items = items.Where(x => x.Title.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0 || x.App.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
            // Auto option: its own list above the table, outside sorting and filtering
            IntPtr autoNow = App.I.FirstAvailableWindow(IntPtr.Zero);
            var autoPip = App.I.Pips.FirstOrDefault(p => !p.IsClosing && p.St.Auto);
            if (autoPip != null && autoPip.Src != IntPtr.Zero) autoNow = autoPip.Src;
            var autoRow = new WinRowItem
            {
                IsAuto = true,
                Hwnd = autoNow,
                Title = "Auto",
                App = "Starts on the first available window, then switches to the one you last used. Skips minimized and closed windows.",
                SizeText = "\u2014",
                Status = autoPip != null ? "In PiP" : "",
                Glyph = "\uE895",
                Order = -1
            };
            Func<WinRowItem, IComparable> keyOf = x => sortKey == "name" ? (IComparable)x.Title.ToLowerInvariant()
                : sortKey == "app" ? x.App.ToLowerInvariant()
                : sortKey == "size" ? (IComparable)(x.SizeText == "—" ? 0L : (long)ParseArea(x.SizeText))
                : sortKey == "status" ? (IComparable)x.Status
                : x.Order;
            items = (sortDesc ? items.OrderByDescending(keyOf) : items.OrderBy(keyOf)).ToList();

            string aSig = autoRow.Hwnd + ":" + autoRow.Status;
            if (aSig != autoSig)
            {
                autoSig = aSig;
                bool autoWasSel = autoList.SelectedItem != null;
                syncingSel = true;
                autoList.ItemsSource = new[] { autoRow };
                if (autoWasSel) autoList.SelectedIndex = 0;
                syncingSel = false;
                if (autoWasSel) OnWinSelected();
            }

            string sig = "n" + items.Count + "|" + string.Join("|", items.Select(x => x.Hwnd + ":" + x.Title + ":" + x.App + ":" + x.SizeText + ":" + x.Status + ":" + x.IsDisplay + ":" + x.IsAuto));
            if (sig == winSig) return;
            winSig = sig;
            foreach (var x in items) if (!x.IsDisplay && !x.IsAuto) { x.Icon = IconFor(x.Hwnd); x.MayFreeze = MayFreeze(x.Hwnd); }

            var prev = winList.SelectedItem as WinRowItem;
            winList.ItemsSource = items;
            WinRowItem again = null;
            if (prev != null) again = items.FirstOrDefault(x => prev.IsAuto ? x.IsAuto : prev.IsDisplay ? (x.IsDisplay && x.ScreenBounds == prev.ScreenBounds) : (!x.IsAuto && !x.IsDisplay && x.Hwnd == prev.Hwnd));
            if (again == null && items.Count > 0 && prev == null && autoList.SelectedItem == null) again = items[0];
            syncingSel = true;
            winList.SelectedItem = again;
            syncingSel = false;
            OnWinSelected();
        }

        /// <summary>The selected Windows-page entry: the Auto option or a row in the table.</summary>
        static WinRowItem SelItem()
        {
            if (autoList != null && autoList.SelectedItem is WinRowItem) return (WinRowItem)autoList.SelectedItem;
            return winList == null ? null : winList.SelectedItem as WinRowItem;
        }

        static double ParseArea(string s)
        {
            var p = s.Split('×');
            int a, b;
            if (p.Length == 2 && int.TryParse(p[0].Trim(), out a) && int.TryParse(p[1].Trim(), out b)) return (double)a * b;
            return 0;
        }

        static IntPtr shownSel = IntPtr.Zero;
        static bool shownSelDisplay, shownSelAuto;

        static void OnWinSelected()
        {
            if (winList == null) return;
            var it = SelItem();
            bool same = it != null && it.Hwnd == shownSel && it.IsDisplay == shownSelDisplay && it.IsAuto == shownSelAuto;
            shownSel = it == null ? IntPtr.Zero : it.Hwnd;
            shownSelDisplay = it != null && it.IsDisplay;
            shownSelAuto = it != null && it.IsAuto;
            selTitle.Text = it == null ? "Nothing selected" : it.Title;
            selSub.Text = it == null ? "Pick a window in the list."
                : it.IsAuto ? "Starts on the first available window, then switches to the one you were last using, even with Follow turned off."
                    + (it.Hwnd != IntPtr.Zero ? " Right now: " + U.Trunc(U.WindowTitle(it.Hwnd), 48) : "")
                : it.IsDisplay ? "Mirrors this whole display" : it.App + (it.Status.Length > 0 ? " · " + it.Status : "");
            previewNote.Text = it == null ? "Live preview" : it.IsAuto && it.Hwnd == IntPtr.Zero ? "Waiting for a window" : it.IsDisplay ? "" : Native.IsIconic(it.Hwnd) ? "Minimized — it will be restored" : "";
            previewNote.FontFamily = it != null && it.IsDisplay ? Fluent.IconFont : Fluent.TextFont;
            previewNote.FontSize = it != null && it.IsDisplay ? 40 : 12;
            BuildCommands();
            if (!same)
            {
                RefreshFixState(it);
                HideThumb();
                if (detailsCard.Visibility == Visibility.Visible) { Anim.Enter(selTitle, 6, 0, 200); Anim.Enter(selSub, 6, 30, 200); }
            }
            UpdatePreview();
        }

        static void OpenSelected()
        {
            var it = SelItem();
            if (it == null) return;
            if (it.IsAuto) App.I.CreateAutoPip();
            else if (it.IsDisplay) App.I.UseScreen(it.ScreenBounds, null);
            else if (Native.IsWindow(it.Hwnd)) App.I.UseWindow(it.Hwnd, null);
            winSig = "";
            DelayRun(300, RefreshWindows);
        }

        // live preview: a DWM thumbnail drawn onto this window over the preview box; it fades in
        static void HideThumb()
        {
            if (thumb != IntPtr.Zero) { Native.DwmUnregisterThumbnail(thumb); thumb = IntPtr.Zero; }
            thumbSrc = IntPtr.Zero;
            thumbRect = System.Drawing.Rectangle.Empty;
            thumbAlpha = 0;
        }

        static void FadeThumbIn()
        {
            thumbAlpha = Anim.On ? Math.Min(255, thumbAlpha + 26) : 255;
            var props = new Native.DWM_THUMBNAIL_PROPERTIES { dwFlags = Native.DWM_TNP_OPACITY, opacity = (byte)thumbAlpha };
            Native.DwmUpdateThumbnailProperties(thumb, ref props);
        }

        static void UpdatePreview()
        {
            if (w == null || previewBox == null) return;
            var it = SelItem();
            bool want = !App.S.LiteMode && !sizing && w.IsActive && page == "windows" && it != null && !it.IsDisplay && previewBox.IsVisible && w.WindowState != WindowState.Minimized
                        && Native.IsWindow(it.Hwnd) && !Native.IsIconic(it.Hwnd)
                        && pageWindows.Opacity > 0.99 && detailsCard.Opacity > 0.99 && !IsMoving(pageWindows) && !IsMoving(detailsCard);
            if (!want) { HideThumb(); return; }
            IntPtr hwnd = new WindowInteropHelper(w).Handle;
            if (hwnd == IntPtr.Zero) return;
            if (thumbSrc != it.Hwnd)
            {
                HideThumb();
                IntPtr t;
                if (Native.DwmRegisterThumbnail(hwnd, it.Hwnd, out t) != 0) return;
                thumb = t;
                thumbSrc = it.Hwnd;
                thumbAlpha = 0;
            }
            System.Drawing.Rectangle box;
            try
            {
                Rect rb = previewBox.TransformToAncestor(w).TransformBounds(new Rect(0, 0, previewBox.ActualWidth, previewBox.ActualHeight));   // includes the controller-mode zoom
                var dpi = VisualTreeHelper.GetDpi(w);
                int pad = (int)(8 * uiScale * dpi.DpiScaleX);
                box = new System.Drawing.Rectangle((int)Math.Round(rb.X * dpi.DpiScaleX) + pad, (int)Math.Round(rb.Y * dpi.DpiScaleY) + pad,
                    (int)Math.Round(rb.Width * dpi.DpiScaleX) - 2 * pad, (int)Math.Round(rb.Height * dpi.DpiScaleY) - 2 * pad);
            }
            catch { return; }
            Native.SIZE sz;
            if (Native.DwmQueryThumbnailSourceSize(thumb, out sz) != 0 || sz.cx < 2 || sz.cy < 2) return;
            var dst = U.FitRect(new System.Drawing.Size(sz.cx, sz.cy), box);
            if (dst == thumbRect) return;
            thumbRect = dst;
            var props = new Native.DWM_THUMBNAIL_PROPERTIES
            {
                dwFlags = Native.DWM_TNP_RECTDESTINATION | Native.DWM_TNP_VISIBLE | Native.DWM_TNP_OPACITY,
                rcDestination = Native.RECT.From(dst),
                opacity = (byte)thumbAlpha,
                fVisible = true
            };
            Native.DwmUpdateThumbnailProperties(thumb, ref props);
        }

        static System.Windows.Media.Effects.Effect padGlow;

        /// <summary>The XAML with a solid background instead of Mica (parsed this way so Lite mode never flashes Mica).</summary>
        internal static string LiteXaml(string xaml)
        {
            return xaml.Replace("WindowBackdropType='Mica'", "WindowBackdropType='None' Background='{DynamicResource AppBg}'");
        }

        /// <summary>Applies Lite mode to the open window: backdrop, shadows and the live preview.</summary>
        static void ApplyLite()
        {
            if (w == null) return;
            bool lite = App.S.LiteMode;
            var fw = w as Wpf.Ui.Controls.FluentWindow;
            bool solid = Looks.Solid;                                     // Lite mode, or the Xbox look (no Mica there)
            try { if (fw != null) fw.WindowBackdropType = solid ? Wpf.Ui.Controls.WindowBackdropType.None : Wpf.Ui.Controls.WindowBackdropType.Mica; } catch { }
            if (solid) w.SetResourceReference(Control.BackgroundProperty, "AppBg");
            else w.Background = Brushes.Transparent;
            if (padRing != null) padRing.Effect = lite ? null : padGlow;
            if (lite && navPane != null) navPane.Effect = null;
            if (previewBox != null) previewBox.Visibility = lite ? Visibility.Collapsed : Visibility.Visible;
            HideThumb();
            if (!lite) DelayRun(100, UpdatePreview);
        }

        static bool IsMoving(UIElement el)
        {
            var tt = el.RenderTransform as TranslateTransform;
            return tt != null && Math.Abs(tt.Y) > 0.5;
        }

        static void RefreshFixState(WinRowItem it)
        {
            if (chromiumCard == null) return;
            int token = ++fixToken;
            if (it == null || it.IsDisplay || it.IsAuto) { chromiumCard.Visibility = Visibility.Collapsed; return; }
            string exe = FrozenFix.ExePath(it.Hwnd);
            if (!FrozenFix.LooksChromium(exe)) { chromiumCard.Visibility = Visibility.Collapsed; return; }
            if (chromiumCard.Visibility != Visibility.Visible) { chromiumCard.Visibility = Visibility.Visible; Anim.Enter(chromiumCard, 8, 60, 260); }
            fixAlways.IsEnabled = false;
            fixState.Text = "Looking for this app's shortcuts…";
            Task.Run(() =>
            {
                var files = FrozenFix.FindShortcuts(exe);
                int with = FrozenFix.CountWithFlag(files);
                Fluent.App.Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (token != fixToken || fixAlways == null) return;
                    fixSync = true;
                    fixAlways.IsChecked = files.Count > 0 && with == files.Count;
                    fixSync = false;
                    fixAlways.IsEnabled = files.Count > 0;
                    fixState.Text = files.Count == 0 ? "No desktop, Start or taskbar shortcut was found for this app."
                        : files.Count + (files.Count == 1 ? " shortcut found." : " shortcuts found.") + " Takes effect the next time you start the app from one of them.";
                }));
            });
        }

        /// <summary>Chromium-based apps (Chrome, Edge, Discord, Spotify...) can freeze in a PiP: flagged with a warning icon on their row.</summary>
        static bool MayFreeze(IntPtr h)
        {
            string exe = FrozenFix.ExePath(h);
            if (exe == null) return false;
            bool v;
            if (!chromiumExe.TryGetValue(exe, out v)) { v = FrozenFix.LooksChromium(exe); chromiumExe[exe] = v; }
            return v;
        }

        /// <summary>The freeze fix with both options: restart now, or always start the app with the fix.</summary>
        static void ShowFreezeDialog(WinRowItem it)
        {
            if (it == null || it.IsDisplay || it.IsAuto) return;
            string exe = FrozenFix.ExePath(it.Hwnd);
            string name = U.ProcessName(it.Hwnd);
            var extra = new StackPanel { Margin = new Thickness(0, 16, 0, 0) };
            var always = new Wpf.Ui.Controls.ToggleSwitch { Content = "Always start " + name + " with the fix", IsEnabled = false };
            var state = Fluent.Label("Looking for this app's shortcuts…", 12, "Tertiary", true);
            state.Margin = new Thickness(0, 6, 0, 0);
            extra.Children.Add(always); extra.Children.Add(state);
            bool sync = true;
            Action load = () => Task.Run(() =>
            {
                var files = exe == null ? new List<string>() : FrozenFix.FindShortcuts(exe);
                int with = FrozenFix.CountWithFlag(files);
                Fluent.App.Dispatcher.BeginInvoke(new Action(() =>
                {
                    sync = true;
                    always.IsChecked = files.Count > 0 && with == files.Count;
                    sync = false;
                    always.IsEnabled = files.Count > 0;
                    state.Text = files.Count == 0 ? "No desktop, Start or taskbar shortcut was found for this app."
                        : files.Count + (files.Count == 1 ? " shortcut found." : " shortcuts found.") + " Takes effect the next time you start the app from one of them.";
                }));
            });
            always.Click += (o, e) =>
            {
                if (sync || exe == null) return;
                bool on = always.IsChecked == true;
                always.IsEnabled = false;
                state.Text = on ? "Adding the fix to the shortcuts…" : "Removing the fix from the shortcuts…";
                Task.Run(() =>
                {
                    var r = FrozenFix.EditShortcuts(FrozenFix.FindShortcuts(exe), on);
                    Fluent.App.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        string msg = (on ? "Added the fix to " : "Removed the fix from ") + r.Item1 + (r.Item1 == 1 ? " shortcut." : " shortcuts.");
                        if (r.Item2 > 0) msg += " " + r.Item2 + " couldn't be changed (all-users shortcuts need administrator rights).";
                        state.Text = msg;
                        always.IsEnabled = true;
                        fixToken++; RefreshFixState(SelItem());
                    }));
                });
            };
            load();
            IntPtr h = it.Hwnd;
            Ask("The picture may freeze",
                name + " stops drawing while other windows cover it, so its PiP can freeze. Restarting it with a fix keeps it drawing. Anything unsaved in it will be lost.",
                "Restart with the fix", "Close", yes =>
                {
                    if (!yes) return;
                    FrozenFix.RelaunchNow(h);
                    DelayRun(5000, () => { winSig = ""; RefreshWindows(); });
                }, true, extra, always);
        }

        static void ApplyAlwaysFix(bool on)
        {
            if (fixSync) return;
            var it = SelItem();
            if (it == null || it.IsDisplay) return;
            string exe = FrozenFix.ExePath(it.Hwnd);
            if (exe == null) return;
            fixAlways.IsEnabled = false;
            fixState.Text = on ? "Adding the fix to the shortcuts…" : "Removing the fix from the shortcuts…";
            Task.Run(() =>
            {
                var r = FrozenFix.EditShortcuts(FrozenFix.FindShortcuts(exe), on);
                Fluent.App.Dispatcher.BeginInvoke(new Action(() =>
                {
                    string msg = (on ? "Added the fix to " : "Removed the fix from ") + r.Item1 + (r.Item1 == 1 ? " shortcut." : " shortcuts.");
                    if (r.Item2 > 0) msg += " " + r.Item2 + " couldn't be changed (all-users shortcuts need administrator rights).";
                    if (fixState != null) fixState.Text = msg;
                    if (winList != null) { fixToken++; RefreshFixState(SelItem()); }
                }));
            });
        }

        // ----------------------------------------------------------------------------- running page
        static IntPtr SelectedExternal()
        {
            var it = runList == null ? null : runList.SelectedItem as PipRowItem;
            return it != null && it.Pip == null && Native.IsWindow(it.External) ? it.External : IntPtr.Zero;
        }

        // ------------------------------------------------------------ options for one PiP (Running PiPs: ⋯ or right-click)
        static System.Windows.Controls.Primitives.Popup optPopup;

        /// <summary>A small card at the pointer with this PiP's own options: opacity, volume, size, shape, black bars and snapping.
        /// Works for PiP Studio's PiPs and other apps' PiPs alike; changes apply right away.</summary>
        static void ShowPipOptions(PipRowItem row)
        {
            if (optPopup != null) optPopup.IsOpen = false;
            var pip = row.Pip;
            IntPtr ext = row.External;
            if ((pip == null || pip.IsClosing) && (ext == IntPtr.Zero || !Native.IsWindow(ext))) return;
            bool own = pip != null;

            var sp = new StackPanel();
            var title = Fluent.Label(row.Title, 14, "Text", false);
            title.FontWeight = FontWeights.SemiBold;
            title.TextTrimming = TextTrimming.CharacterEllipsis;
            sp.Children.Add(title);
            var sub = Fluent.Label(own ? row.App : row.App + " · " + L.T("App's own PiP"), 12, "Sub", false);
            sub.Margin = new Thickness(0, 2, 0, 10);
            sp.Children.Add(sub);

            Action<string, FrameworkElement> add = (label, ctl) =>
            {
                var g = new Grid { Margin = new Thickness(0, 6, 0, 0), MinHeight = 32 };
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(112) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                var l = Fluent.Label(label, 13, "Text", false);
                l.VerticalAlignment = VerticalAlignment.Center;
                g.Children.Add(l);
                Grid.SetColumn(ctl, 1);
                ctl.VerticalAlignment = VerticalAlignment.Center;
                g.Children.Add(ctl);
                sp.Children.Add(g);
            };
            Func<int, int, int, string, Action<int>, FrameworkElement> slider = (min, max, val, suffix, set) =>
            {
                var dp = new DockPanel();
                var v = Fluent.Label(val + suffix, 13, "Sub", false);
                v.Width = 44; v.TextAlignment = TextAlignment.Right;
                DockPanel.SetDock(v, Dock.Right);
                dp.Children.Add(v);
                var sl = new Slider { Minimum = min, Maximum = max, Value = val, IsSnapToTickEnabled = true, TickFrequency = 1, VerticalAlignment = VerticalAlignment.Center };
                sl.ValueChanged += (o, e) => { v.Text = (int)sl.Value + suffix; set((int)sl.Value); };
                dp.Children.Add(sl);
                return dp;
            };
            Func<string[], int, Action<int>, FrameworkElement> combo = (items, val, set) =>
            {
                var cb = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
                foreach (var i in items) cb.Items.Add(L.Plain(L.T(i)));
                L.Combo(cb);
                cb.SelectedIndex = U.Clamp(val, 0, items.Length - 1);
                cb.SelectionChanged += (o, e) => { if (cb.SelectedIndex >= 0) set(cb.SelectedIndex); };
                return cb;
            };

            // opacity
            int op = own ? (int)Math.Round(pip.St.Opacity * 100) : ExternalPip.OpacityPercent(ext);
            add("Opacity", slider(own ? 10 : 20, 100, op, "%", v => { if (own) pip.SetOpacity(v / 100.0); else ExternalPip.SetOpacityPercent(ext, v); }));

            // volume (the app behind it)
            string proc = own ? pip.St.ProcessName : U.ProcessName(ext);
            float? vol = string.IsNullOrEmpty(proc) || (own && pip.St.Kind != SourceKind.Window) ? null : Audio.GetVolume(proc);
            if (vol.HasValue)
                add("Volume", slider(0, 100, (int)Math.Round(vol.Value * 100), "%", v => { if (own) pip.SetVolume(v / 100f); else ExternalPip.SetVolumeLevel(ext, v / 100f); }));
            else
            {
                var none = Fluent.Label("No sound from this app right now", 12, "Sub", false);
                none.TextWrapping = TextWrapping.Wrap;
                add("Volume", none);
            }

            // size, shape, black bars, snapping
            add("Size", combo(SizePresets.Names, own ? pip.PresetIndex : ExternalPip.PresetIndex(ext),
                i => { if (own) pip.SetSizePreset(i); else ExternalPip.SetSizePreset(ext, i); }));
            add("Shape", combo(AspectChoices.Names, own ? pip.St.Aspect : ExternalPip.AspectOf(ext),
                i => { if (own) pip.SetAspect(i); else ExternalPip.SetAspect(ext, i); }));
            var trim = new Wpf.Ui.Controls.ToggleSwitch { IsChecked = own ? pip.St.TrimBars : ExternalPip.TrimsBars(ext), HorizontalAlignment = HorizontalAlignment.Left };
            trim.Click += (o, e) => { bool on = trim.IsChecked == true; if (own) pip.SetTrimBars(on); else ExternalPip.SetTrim(ext, on); };
            add("Black bars", trim);
            var trimNote = Fluent.Label(own ? "Crops away the bars around the video" : "Fits the window to the video so the bars disappear", 12, "Sub", false);
            trimNote.TextWrapping = TextWrapping.Wrap;
            trimNote.Margin = new Thickness(112, 0, 0, 0);
            sp.Children.Add(trimNote);
            add("Snapping", combo(new[] { "Follow my setting", "Always snap to spots", "Never snap" }, own ? pip.St.Snap : ExternalPip.SnapModeOf(ext),
                i => { if (own) pip.SetSnap(i); else ExternalPip.SetSnapMode(ext, i); }));

            var card = new Border
            {
                Width = 340, Padding = new Thickness(16, 14, 16, 16), CornerRadius = Looks.CR(8), BorderThickness = new Thickness(1),
                Child = sp, Margin = new Thickness(12),
                Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 24, ShadowDepth = 4, Opacity = 0.28, Color = Colors.Black }
            };
            card.SetResourceReference(Border.BackgroundProperty, "AppBg");
            card.SetResourceReference(Border.BorderBrushProperty, "CardBorder");
            card.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, "Text");
            optPopup = new System.Windows.Controls.Primitives.Popup
            {
                Child = card, StaysOpen = false, AllowsTransparency = true,
                Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint,
                PopupAnimation = App.S.LiteMode ? System.Windows.Controls.Primitives.PopupAnimation.None : System.Windows.Controls.Primitives.PopupAnimation.Fade,
                PlacementTarget = runList
            };
            optPopup.Closed += (o, e) => { runSig = ""; RefreshRunning(); };
            optPopup.IsOpen = true;
            L.Walk(card);
        }

        static PipWindow SelectedPip()
        {
            var it = runList == null ? null : runList.SelectedItem as PipRowItem;
            if (it == null || it.Pip == null || it.Pip.IsClosing) return null;
            return it.Pip;
        }

        static string SpotName(PipWindow p)
        {
            if (p.InTheater) return "Theater";
            switch (p.NearestSpot(true))
            {
                case "TL": return "Top left"; case "T": return "Top"; case "TR": return "Top right";
                case "L": return "Left"; case "R": return "Right";
                case "BL": return "Bottom left"; case "B": return "Bottom"; default: return "Bottom right";
            }
        }

        /// <summary>Running PiPs > Marked as PiPs: one row per Treat as a PiP you chose, each with Remove.</summary>
        static void RefreshMarked()
        {
            if (markedList == null) return;
            var marks = ExternalPip.Marks();
            string sig = string.Join("|", marks.Select(m => m.Hwnd.ToInt64() + m.Key + m.Title));
            markedBox.Visibility = marks.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            if (sig == markedSig) return;
            markedSig = sig;
            markedList.Children.Clear();
            foreach (var mk in marks)
            {
                var m = mk;
                var row = new Grid { Margin = new Thickness(0, 0, 0, 4) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var bg = new Border { CornerRadius = Looks.CR(4), BorderThickness = new Thickness(1), Padding = new Thickness(14, 10, 12, 10) };
                bg.SetResourceReference(Border.BackgroundProperty, "Card");
                bg.SetResourceReference(Border.BorderBrushProperty, "CardBorder");
                Grid.SetColumnSpan(bg, 2);
                row.Children.Add(bg);
                var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 8, 12, 8) };
                text.Children.Add(Fluent.Label(m.Title, 14, "Text", false));
                text.Children.Add(Fluent.Label(m.WholeKind ? "Every window like this from " + m.App : m.App + " · this window only, until it closes", 12, "Sub", false));
                row.Children.Add(text);
                var remove = PlainButton("Remove", () =>
                {
                    ExternalPip.Unmark(m);                                   // a normal window again, off always on top
                    runSig = ""; winSig = ""; RefreshRunning(); RefreshMarked(); BuildCommands();
                });
                remove.VerticalAlignment = VerticalAlignment.Center;
                remove.Margin = new Thickness(0, 0, 12, 0);
                Grid.SetColumn(remove, 1);
                row.Children.Add(remove);
                markedList.Children.Add(row);
            }
        }

        static void RefreshRunning()
        {
            if (runList == null) return;
            RefreshMarked();
            var items = new List<PipRowItem>();
            string q = "";
            foreach (var p in App.I.Pips.Where(x => !x.IsClosing))
            {
                var modes = new List<string>();
                if (p.St.ClickThrough) modes.Add("Ghost");
                if (p.St.Auto) modes.Add("Auto");
                else if (p.St.Follow) modes.Add("Follows");
                if (p.IsParked) modes.Add("Source hidden");
                if (p.St.Locked) modes.Add("Locked");
                if (!p.HasSource) modes.Add("Waiting");
                if (App.I.HiddenAll) modes.Add("Hidden");
                var b = p.TargetBounds;
                var item = new PipRowItem
                {
                    Pip = p,
                    Title = p.DisplayName,
                    App = p.St.Kind == SourceKind.Screen ? "Screen" : p.St.ProcessName,
                    Position = SpotName(p),
                    SizeText = b.Width + " × " + b.Height,
                    OpacityText = (int)Math.Round(p.St.Opacity * 100) + "%",
                    Mode = modes.Count == 0 ? "Normal" : string.Join(", ", modes),
                    Glyph = p.St.Kind == SourceKind.Screen ? "" : null,
                    Icon = p.St.Kind == SourceKind.Window && p.Src != IntPtr.Zero ? IconFor(p.Src) : null
                };
                if (q.Length > 0 && item.Title.IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0 && (item.App ?? "").IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0) continue;
                items.Add(item);
            }
            // other apps' own PiP windows
            if (App.S.ControlOtherPips)
                foreach (var h in ExternalPip.FindAll())
                {
                    var b = U.WindowRect(h);
                    string proc = U.ProcessName(h);
                    items.Add(new PipRowItem
                    {
                        External = h,
                        Title = ExternalPip.AppName(h) + "'s own PiP",
                        App = proc,
                        Position = ExternalPip.SpotName(h),
                        SizeText = b.Width + " × " + b.Height,
                        OpacityText = (int)Math.Round(ExternalPip.OpacityOf(h) * 100) + "%",
                        Mode = "App's own PiP",
                        Icon = IconFor(h)
                    });
                }
            // ...and the ones that are minimized: still yours, just out of sight (Restore brings them back)
            if (App.S.ControlOtherPips)
                foreach (var h in ExternalPip.MinimizedPips())
                    items.Add(new PipRowItem
                    {
                        External = h,
                        Title = ExternalPip.AppName(h) + "'s own PiP",
                        App = U.ProcessName(h),
                        Position = "—",
                        SizeText = "—",
                        OpacityText = (int)Math.Round(ExternalPip.OpacityOf(h) * 100) + "%",
                        Mode = "Minimized",
                        Icon = IconFor(h)
                    });
            // never empty: "" means "refresh needed", and an empty list must still replace the old rows
            string sig = "n" + items.Count + "|" + string.Join("|", items.Select(x => (x.Pip != null ? x.Pip.GetHashCode() : x.External.ToInt64()) + x.Title + x.Position + x.SizeText + x.OpacityText + x.Mode));
            // the empty message only when the list really is empty
            bool showEmpty = items.Count == 0;
            runEmpty.Text = showEmpty ? "No PiPs are open.\nPick a window on the Windows page, or press " + HK.Display("HK_NewPip") + " anywhere." : "";
            var emptyPanel = F<StackPanel>("RunEmptyPanel");
            runList.Visibility = showEmpty ? Visibility.Collapsed : Visibility.Visible;
            if (showEmpty && emptyPanel.Visibility != Visibility.Visible) { emptyPanel.Visibility = Visibility.Visible; Anim.Enter(emptyPanel, 12, 60, 300); }
            else if (!showEmpty) emptyPanel.Visibility = Visibility.Collapsed;
            if (sig == runSig) return;
            runSig = sig;
            var prev = SelectedPip();
            var prevExt = SelectedExternal();
            runList.ItemsSource = items;
            var again = items.FirstOrDefault(x => prev != null ? x.Pip == prev : prevExt != IntPtr.Zero && x.External == prevExt) ?? (items.Count > 0 ? items[0] : null);
            runList.SelectedItem = again;
            BuildCommands();
        }

        // ----------------------------------------------------------------------------- search
        static void OnSearch()
        {
            UpdateSearchHint();
            string q = searchBox.Text.Trim();
            if (q.Length == 0)
            {
                if (page == "search") Navigate(lastSettingsPage);
                return;
            }
            var sp = pages["search"];
            sp.Children.Clear();
            var words = q.ToLowerInvariant().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var hits = searchIndex.Where(s => { string hay = (s.Title + " " + s.Text + " " + s.Page).ToLowerInvariant(); return words.All(x => hay.Contains(x)); })
                                  .OrderBy(s => s.Title.ToLowerInvariant().Contains(words[0]) ? 0 : 1).Take(40).ToList();
            if (hits.Count == 0)
            {
                var none = Fluent.Label("No settings match “" + q + "”. Try a different word.", 14, "Sub");
                none.Margin = new Thickness(2, 4, 0, 0);
                sp.Children.Add(none);
            }
            foreach (var h in hits)
            {
                var hit = h;
                var b = ClickRow(sp, "", hit.Title, navLabels[Array.IndexOf(navKeys, hit.Page)], () => GoToSetting(hit));
            }
            if (page != "search")
            {
                page = "search";
                pageTitle.Text = "Search results";
                pageWindows.Visibility = pageRunning.Visibility = Visibility.Collapsed;
                pageScroll.Visibility = Visibility.Visible;
                foreach (var kv in pages) kv.Value.Visibility = kv.Key == "search" ? Visibility.Visible : Visibility.Collapsed;
                foreach (var nb in navBtns) nb.IsChecked = false;
                MoveIndicator(true);
                BuildCommands();
            }
            AnimatePage();
        }

        /// <summary>Glide the settings page so the element is in view (like BringIntoView, but animated).</summary>
        static void ScrollIntoViewSmooth(FrameworkElement el)
        {
            if (pageScroll == null || el == null || !el.IsVisible) { if (el != null) el.BringIntoView(); return; }
            double y;
            try { y = el.TranslatePoint(new Point(0, 0), pageScroll).Y; } catch { el.BringIntoView(); return; }
            double view = pageScroll.ViewportHeight, h = el.ActualHeight, m = 24;
            if (y >= m && y + h <= view - m) return;                     // already fully visible
            SmoothScroll.To(pageScroll, pageScroll.VerticalOffset + y - m);
        }

        static void GoToSetting(SearchEntry hit)
        {
            searchBox.Text = "";
            Navigate(hit.Page);
            if (hit.Reveal != null) hit.Reveal();
            DelayRun(Anim.On ? 380 : 30, () =>
            {
                if (w == null) return;
                ScrollIntoViewSmooth(hit.Element);
                Flash(hit.Element);
            });
        }

        /// <summary>Brief accent outline so the eye lands on the setting you searched for.</summary>
        static void Flash(FrameworkElement el)
        {
            var host = el as Panel;
            if (host is StackPanel && ((StackPanel)host).Children.Count > 0) host = ((StackPanel)host).Children[0] as Panel;   // expander: ring its header
            var ring = new Border { CornerRadius = Looks.CR(4), BorderThickness = new Thickness(2), IsHitTestVisible = false, Opacity = 0 };
            ring.SetResourceReference(Border.BorderBrushProperty, "Accent");
            if (host == null)
            {
                var b = el as Border;
                if (b == null || !(b.Child is Panel)) return;
                host = (Panel)b.Child;
            }
            if (host is Grid) { Grid.SetColumnSpan(ring, 99); Grid.SetRowSpan(ring, 99); }
            ring.Margin = new Thickness(-1);
            host.Children.Add(ring);
            Anim.Fade(ring, 1, 150, 0, () => Anim.Fade(ring, 0, 600, 900, () => host.Children.Remove(ring)));
        }

        // ----------------------------------------------------------------------------- key capture
        static void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (captureKb != null)
            {
                var k = e.Key == Key.System ? e.SystemKey : e.Key;
                if (k == Key.LeftCtrl || k == Key.RightCtrl || k == Key.LeftAlt || k == Key.RightAlt || k == Key.LeftShift || k == Key.RightShift || k == Key.LWin || k == Key.RWin || k == Key.ImeProcessed) return;
                e.Handled = true;
                if (k == Key.Escape) { EndKeyCapture(); return; }
                if (k == Key.Back || k == Key.Delete) { HK.Set(App.S, captureKb, ""); EndKeyCapture(); return; }
                int mods = 0;
                var mm = Keyboard.Modifiers;
                if ((mm & ModifierKeys.Alt) != 0) mods |= 1;
                if ((mm & ModifierKeys.Control) != 0) mods |= 2;
                if ((mm & ModifierKeys.Shift) != 0) mods |= 4;
                if ((mm & ModifierKeys.Windows) != 0) mods |= 8;
                if (mods == 0 || mods == 4) { captureKbBtn.Content = "Add Ctrl, Alt or Win…"; return; }
                HK.Set(App.S, captureKb, HK.Spec(mods, KeyInterop.VirtualKeyFromKey(k)));
                EndKeyCapture();
                return;
            }
            if (e.Key == Key.Escape && overlayOpen) { overlayOpen = false; ApplyLayout(true); e.Handled = true; return; }
            if ((e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control) || (e.Key == Key.E && Keyboard.Modifiers == ModifierKeys.Control))
            {
                if (page == "windows" && winSearch != null) { winSearch.Focus(); winSearch.SelectAll(); e.Handled = true; return; }
                if (compact) OpenPane();
                searchBox.Focus(); searchBox.SelectAll(); e.Handled = true;
            }
            else if (e.Key == Key.F5) { winSig = runSig = ""; RefreshWindows(); RefreshRunning(); e.Handled = true; }
        }

        static void BeginKeyCapture(string field, Button btn)
        {
            if (captureKb != null) EndKeyCapture();
            App.I.UnregisterHotkeys();
            captureKb = field;
            captureKbBtn = btn;
            btn.Content = "Press keys…";
            w.Focus();
        }

        static void EndKeyCapture()
        {
            var btn = captureKbBtn;
            string field = captureKb;
            captureKb = null;
            captureKbBtn = null;
            App.I.SettingsChanged(true);
            if (btn != null && field != null) btn.Content = KeyText(field);
        }

        static string KeyText(string field) { string d = HK.Display(field); return d.Length == 0 ? "Not set" : d.Replace("+", " + "); }

        // ----------------------------------------------------------------------------- Settings building blocks
        //  SettingsCard: 4 px radius, 68 min height, 16 px padding, 20 px icon then 16 px gap, body + caption text.
        //  SettingsExpander: header card with a chevron; rows below on a secondary fill, separated by hairlines.
        static StackPanel NewPage(string key)
        {
            var sp = new StackPanel { Visibility = Visibility.Collapsed };
            settingsHost.Children.Add(sp);
            pages[key] = sp;
            buildingPage = key;
            buildingReveal = null;
            return sp;
        }

        static void Section(StackPanel p, string text)
        {
            var t = Fluent.Label(text, 14, "Text");
            t.FontWeight = FontWeights.SemiBold;
            t.Margin = new Thickness(1, p.Children.Count == 0 ? 2 : 30, 0, 8);
            p.Children.Add(t);
        }

        static void Note(StackPanel p, string text)
        {
            var t = Fluent.Label(text, 12, "Sub");
            t.Margin = new Thickness(1, 0, 0, 12);
            p.Children.Add(t);
        }

        static void Index(string title, string text, FrameworkElement el, Action reveal = null)
        {
            AttachExplainer(title, el);
            searchIndex.Add(new SearchEntry { Title = title, Text = text ?? "", Page = buildingPage, Element = el, Reveal = reveal });
        }

        /// <summary>The icon / title / description / control layout every card and row uses.</summary>
        static string lastDesc;      // the full description of the setting being built (shown under its explainer)

        /// <summary>Short descriptions shown next to settings; the full text moves to the explanations panel.</summary>
        static readonly Dictionary<string, string> ShortDesc = new Dictionary<string, string>
        {
            { "Border", "An outline around each PiP" },
            { "Messages under the PiP", "Show messages below the PiP, not on it" },
            { "When the pointer is over a PiP", "What a PiP does when you point at it" },
            { "Show move arrows", "Arrow buttons that hop to the next spot" },
            { "Fade-out opacity", "How see-through it gets" },
            { "Click-through opacity", "How see-through ghost mode is" },
            { "Lite mode", "Fewer effects, for slower PCs" },
            { "Animation effects", "Glide and fade effects" },
            { "Size limits", "Stop tall or wide apps getting too big" },
            { "Control other apps' PiP windows", "Steer browser and app PiPs too" },
            { "Snap other apps' PiPs to spots", "They land on the same spots as yours" },
            { "Allow more than one PiP at a time", "Off: a new PiP replaces the open one" },
            { "Follow the window I just left", "The PiP shows the window you just left" },
            { "Middle of each edge too", "8 spots instead of 4 corners" },
            { "Stay on one display", "Don't hop to the next display" },
            { "Hide PiPs while a fullscreen app is on the same display", "Includes borderless games" },
            { "Keep minimized sources playing", "The PiP keeps moving when you minimize" },
            { "Controller-friendly mode", "Bigger layout and button hints" },
            { "Fade controller hints when idle", "Hints fade after a few seconds" },
            { "Release the game's focus while controls are on", "The game ignores the controller meanwhile" },
            { "Button assignments", "Click, then press a button. Right-click clears it." },
            { "Hide PiPs from screenshots, recordings and screen sharing", "Costs a little performance" },
            { "Release memory while idle", "Give memory back while in the tray" },
            { "Picture-in-picture for any window or part of the screen", "Uses Windows' own live thumbnails" },
            { "Running as administrator", "Can show admin apps' windows" },
            { "Restart as administrator", "Only needed for admin apps' windows" },
        };

        static Grid CardContent(string glyph, string title, string desc, FrameworkElement control, out TextBlock titleBlock, out TextBlock descBlock)
        {
            lastDesc = desc;
            string sd;
            if (desc != null && title != null && ShortDesc.TryGetValue(title, out sd)) desc = sd;   // the long text goes under the animation
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(glyph == null ? 0 : 36) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            if (glyph != null) g.Children.Add(Fluent.Glyph(glyph, 20));
            var sp = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 16, 0) };
            titleBlock = Fluent.Label(title, 14);
            sp.Children.Add(titleBlock);
            descBlock = null;
            if (!string.IsNullOrEmpty(desc))
            {
                descBlock = Fluent.Label(desc, 12, "Sub");
                descBlock.Margin = new Thickness(0, 1, 0, 0);
                if (lastDesc != null && lastDesc != desc) descBlock.ToolTip = lastDesc;   // the full text, also when explanations are hidden
                sp.Children.Add(descBlock);
            }
            Grid.SetColumn(sp, 1);
            g.Children.Add(sp);
            if (control != null)
            {
                control.VerticalAlignment = VerticalAlignment.Center;
                Grid.SetColumn(control, 2);
                g.Children.Add(control);
            }
            return g;
        }

        static CardParts Card(StackPanel p, string glyph, string title, string desc, FrameworkElement control, string keywords = "")
        {
            TextBlock tb, db;
            var content = CardContent(glyph, title, desc, control, out tb, out db);
            var b = new Border { CornerRadius = Looks.CR(4), BorderThickness = new Thickness(1), Padding = new Thickness(16, 12, 16, 12), MinHeight = 68, Margin = new Thickness(0, 0, 0, 4), Child = content };
            b.SetResourceReference(Border.BackgroundProperty, "Card");
            b.SetResourceReference(Border.BorderBrushProperty, "CardBorder");
            p.Children.Add(b);
            Index(title, (desc ?? "") + " " + keywords + (L.Active ? " " + L.T(title) + " " + L.T(desc ?? "") : ""), b);
            return new CardParts { Root = b, Title = tb, Desc = db };
        }

        /// <summary>A whole-card button (search results, links): fading hover and press like SettingsCard with IsClickEnabled.</summary>
        static Grid ClickRow(StackPanel p, string glyph, string title, string desc, Action click)
        {
            var root = new Grid { Margin = new Thickness(0, 0, 0, 4), Cursor = Cursors.Hand, MinHeight = 68 };
            var bg = new Border { CornerRadius = Looks.CR(4), BorderThickness = new Thickness(1) };
            bg.SetResourceReference(Border.BackgroundProperty, "Card");
            bg.SetResourceReference(Border.BorderBrushProperty, "CardBorder");
            var hov = new Border { CornerRadius = Looks.CR(4) };
            hov.SetResourceReference(Border.BackgroundProperty, "Hover");
            var pr = new Border { CornerRadius = Looks.CR(4) };
            pr.SetResourceReference(Border.BackgroundProperty, "Pressed");
            TextBlock tb, db;
            var chev = Fluent.Glyph("", 12, "Sub");
            var content = CardContent(glyph, title, desc, chev, out tb, out db);
            content.Margin = new Thickness(16, 12, 16, 12);
            root.Children.Add(bg); root.Children.Add(hov); root.Children.Add(pr); root.Children.Add(content);
            Anim.Hover(root, hov, pr);
            root.MouseLeftButtonUp += (o, e) => click();
            MakeActivatable(root, click);
            p.Children.Add(root);
            return root;
        }

        class Expander
        {
            public StackPanel Rows;
            public Action Open;
            public CardParts Header;
        }

        static Expander MakeExpander(StackPanel p, string glyph, string title, string desc, FrameworkElement headerControl, bool startOpen = false, string keywords = "")
        {
            var outer = new StackPanel { Margin = new Thickness(0, 0, 0, 4) };
            var head = new Grid { Cursor = Cursors.Hand, MinHeight = 68 };
            var headBg = new Border { CornerRadius = Looks.CR(4), BorderThickness = new Thickness(1) };
            headBg.SetResourceReference(Border.BackgroundProperty, "Card");
            headBg.SetResourceReference(Border.BorderBrushProperty, "CardBorder");
            var hov = new Border { CornerRadius = Looks.CR(4) };
            hov.SetResourceReference(Border.BackgroundProperty, "Hover");
            var pr = new Border { CornerRadius = Looks.CR(4) };
            pr.SetResourceReference(Border.BackgroundProperty, "Pressed");

            var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            if (headerControl != null) { headerControl.VerticalAlignment = VerticalAlignment.Center; right.Children.Add(headerControl); }
            var chevHost = new Grid { Width = 32, Height = 32, Margin = new Thickness(headerControl != null ? 12 : 0, 0, -6, 0) };
            var chev = Fluent.Glyph("", 12, "Text");
            chev.HorizontalAlignment = HorizontalAlignment.Center;
            var rot = new RotateTransform(0);
            chev.RenderTransform = rot;
            chev.RenderTransformOrigin = new Point(0.5, 0.5);
            chevHost.Children.Add(chev);
            right.Children.Add(chevHost);

            TextBlock tb, db;
            var content = CardContent(glyph, title, desc, right, out tb, out db);
            content.Margin = new Thickness(16, 12, 16, 12);
            head.Children.Add(headBg); head.Children.Add(hov); head.Children.Add(pr); head.Children.Add(content);
            Anim.Hover(head, hov, pr);

            var rows = new StackPanel();
            var rowsHost = new Border { BorderThickness = new Thickness(1, 0, 1, 1), CornerRadius = Looks.CR(0, 0, 4, 4), Visibility = Visibility.Collapsed, ClipToBounds = true, Child = rows };
            rowsHost.SetResourceReference(Border.BackgroundProperty, "CardSecondary");
            rowsHost.SetResourceReference(Border.BorderBrushProperty, "CardBorder");
            outer.Children.Add(head);
            outer.Children.Add(rowsHost);
            p.Children.Add(outer);

            bool open = false;
            Action<bool, bool> set = null;
            set = (wantOpen, animate) =>
            {
                if (wantOpen == open) return;
                open = wantOpen;
                var r0 = Looks.CR(4, 4, wantOpen ? 0 : 4, wantOpen ? 0 : 4);
                headBg.CornerRadius = hov.CornerRadius = pr.CornerRadius = r0;
                bool anim = animate && Anim.On;
                if (anim) rot.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(wantOpen ? 180 : 0, TimeSpan.FromMilliseconds(250)) { EasingFunction = Anim.Smooth });
                else { rot.BeginAnimation(RotateTransform.AngleProperty, null); rot.Angle = wantOpen ? 180 : 0; }
                if (wantOpen)
                {
                    rowsHost.Visibility = Visibility.Visible;
                    if (!anim) { rowsHost.BeginAnimation(FrameworkElement.HeightProperty, null); rowsHost.Height = double.NaN; return; }
                    double width = outer.ActualWidth > 0 ? outer.ActualWidth : 800;
                    rowsHost.Height = double.NaN;
                    rowsHost.Measure(new Size(width, double.PositiveInfinity));
                    double h = rowsHost.DesiredSize.Height;
                    var a = new DoubleAnimation(0, h, TimeSpan.FromMilliseconds(Math.Min(420, 200 + h / 6))) { EasingFunction = Anim.Quint };
                    a.Completed += (o, e) => { if (open) { rowsHost.BeginAnimation(FrameworkElement.HeightProperty, null); rowsHost.Height = double.NaN; } };
                    rowsHost.BeginAnimation(FrameworkElement.HeightProperty, a);
                    rows.Opacity = 0;
                    Anim.Fade(rows, 1, 250, 60);
                }
                else
                {
                    if (!anim) { rowsHost.Visibility = Visibility.Collapsed; return; }
                    var a = new DoubleAnimation(rowsHost.ActualHeight, 0, TimeSpan.FromMilliseconds(180)) { EasingFunction = Anim.Smooth };
                    a.Completed += (o, e) => { if (!open) { rowsHost.Visibility = Visibility.Collapsed; rowsHost.BeginAnimation(FrameworkElement.HeightProperty, null); rowsHost.Height = double.NaN; } };
                    rowsHost.BeginAnimation(FrameworkElement.HeightProperty, a);
                    Anim.Fade(rows, 0, 120);
                }
            };
            head.MouseLeftButtonUp += (o, e) =>
            {
                // clicks on the control in the header (a toggle or a list) don't open the expander
                var d = e.OriginalSource as DependencyObject;
                while (d != null && d != head) { if (d == headerControl) return; d = d is Visual ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d); }
                set(!open, true);
            };
            if (startOpen) set(true, false);
            MakeActivatable(head, () => set(!open, true));

            var ex = new Expander { Rows = rows, Open = () => set(true, false), Header = new CardParts { Root = null, Title = tb, Desc = db } };
            buildingReveal = ex.Open;
            Index(title, (desc ?? "") + " " + keywords + (L.Active ? " " + L.T(title) + " " + L.T(desc ?? "") : ""), outer);
            return ex;
        }

        /// <summary>A row inside an expander: lines up with the header's text, hairline between rows.</summary>
        static Grid Row(Expander ex, string title, string desc, FrameworkElement control, string keywords = "")
        {
            TextBlock tb, db;
            var content = CardContent(null, title, desc, control, out tb, out db);
            var row = new Grid { MinHeight = 52 };
            content.Margin = new Thickness(52, 10, 16, 10);
            if (ex.Rows.Children.Count > 0)
            {
                var line = new Border { Height = 1, VerticalAlignment = VerticalAlignment.Top };
                line.SetResourceReference(Border.BackgroundProperty, "CardBorder");
                row.Children.Add(line);
            }
            row.Children.Add(content);
            ex.Rows.Children.Add(row);
            Index(title, (desc ?? "") + " " + keywords + (L.Active ? " " + L.T(title) + " " + L.T(desc ?? "") : ""), row, ex.Open);
            return row;
        }

        /// <summary>Appearance > PiP window > Title bar buttons: a switch per button, and arrows to move the ones that are on
        /// left or right. The rows follow the title bar's order; buttons that are off are listed after them.</summary>
        static void BuildTitleBarSettings(StackPanel ap)
        {
            var ex = MakeExpander(ap, "\uE737", "Title bar buttons", "Pick the buttons on a PiP's title bar and their order", null, false,
                "title bar buttons toolbar previous next window customize order arrange header");
            var rows = new Dictionary<string, Grid>();
            var lefts = new Dictionary<string, Button>();
            var rights = new Dictionary<string, Button>();
            var switches = new Dictionary<string, Wpf.Ui.Controls.ToggleSwitch>();
            Action relayout = null;

            Action<List<string>> save = on =>
            {
                App.S.TitleBarButtons = string.Join(",", on);
                App.I.SettingsChanged();
                relayout();
            };
            Func<string, string, string, Button> arrow = (key, glyph, tip) =>
            {
                var b = new Wpf.Ui.Controls.Button
                {
                    Content = new TextBlock { Text = glyph, FontFamily = Fluent.IconFont, FontSize = 12 },
                    Appearance = Wpf.Ui.Controls.ControlAppearance.Transparent, BorderThickness = new Thickness(0),
                    Width = 32, Height = 32, Padding = new Thickness(0), Margin = new Thickness(0, 0, 2, 0), ToolTip = tip
                };
                return b;
            };

            var all = TitleBarItems.On(App.S.TitleBarButtons);
            foreach (var k in TitleBarItems.Keys) if (!all.Contains(k)) all.Add(k);
            foreach (var key in all)
            {
                string k = key;
                var l = arrow(k, "\uE76B", "Move left");
                var r = arrow(k, "\uE76C", "Move right");
                var sw = new Wpf.Ui.Controls.ToggleSwitch { IsChecked = false, Margin = new Thickness(10, 0, 0, 0) };
                l.Click += (o, e) =>
                {
                    var on = TitleBarItems.On(App.S.TitleBarButtons);
                    int i = on.IndexOf(k);
                    if (i > 0) { on.RemoveAt(i); on.Insert(i - 1, k); save(on); }
                };
                r.Click += (o, e) =>
                {
                    var on = TitleBarItems.On(App.S.TitleBarButtons);
                    int i = on.IndexOf(k);
                    if (i >= 0 && i < on.Count - 1) { on.RemoveAt(i); on.Insert(i + 1, k); save(on); }
                };
                sw.Click += (o, e) =>
                {
                    var on = TitleBarItems.On(App.S.TitleBarButtons);
                    if (sw.IsChecked == true && !on.Contains(k))
                    {
                        // a button switched on goes just left of More options / Close
                        int at = on.Count;
                        for (int i = 0; i < on.Count; i++) if (on[i] == "more" || on[i] == "close") { at = i; break; }
                        on.Insert(at, k);
                    }
                    else if (sw.IsChecked != true) on.Remove(k);
                    save(on);
                };
                var ctl = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                ctl.Children.Add(l); ctl.Children.Add(r); ctl.Children.Add(sw);
                var label = TitleBarItems.Label(k);
                var row = Row(ex, label, k == TitleBarItems.Required ? "Always shown, so the full menu is never out of reach" : null, ctl, "title bar button " + k);
                // every row gets a divider line; the first one's is hidden when the rows are put in order
                bool hasLine = row.Children.Count > 0 && row.Children[0] is Border && ((Border)row.Children[0]).Height == 1;
                if (!hasLine)
                {
                    var line = new Border { Height = 1, VerticalAlignment = VerticalAlignment.Top };
                    line.SetResourceReference(Border.BackgroundProperty, "CardBorder");
                    row.Children.Insert(0, line);
                }
                rows[k] = row; lefts[k] = l; rights[k] = r; switches[k] = sw;
            }
            var reset = new Button { Content = "Reset", Padding = new Thickness(14, 4, 14, 4) };
            reset.Click += (o, e) => save(TitleBarItems.On(TitleBarItems.Default));
            var resetRow = Row(ex, "Reset title bar buttons", "Back to the default buttons and order", reset, "default restore");

            // a preview of the title bar at the top: your buttons, in your order, redrawn on every change
            var previewIcons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 0, 3, 0) };
            var previewBar = new Grid { Height = 32, Background = new SolidColorBrush(Color.FromRgb(20, 20, 24)) };
            previewBar.Children.Add(new TextBlock { Text = "Your video", Foreground = new SolidColorBrush(Color.FromRgb(235, 235, 242)), FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis });
            previewBar.Children.Add(previewIcons);
            var previewPic = new Border { Height = 46 };
            previewPic.SetResourceReference(Border.BackgroundProperty, "Accent");
            previewPic.Opacity = 0.55;
            var previewPip = new StackPanel { MaxWidth = 460, HorizontalAlignment = HorizontalAlignment.Left };
            previewPip.Children.Add(previewBar);
            previewPip.Children.Add(previewPic);
            var previewClip = new Border { CornerRadius = Looks.CR(6), ClipToBounds = true, Child = previewPip, HorizontalAlignment = HorizontalAlignment.Left };
            var previewRow = new Border { Padding = new Thickness(52, 12, 16, 14), Child = previewClip };
            Action redrawPreview = () =>
            {
                previewIcons.Children.Clear();
                foreach (var k in TitleBarItems.On(App.S.TitleBarButtons))
                    previewIcons.Children.Add(new TextBlock
                    {
                        Text = TitleBarItems.Glyph(k), FontFamily = Fluent.IconFont, FontSize = 13, Width = 30,
                        Foreground = new SolidColorBrush(Color.FromRgb(238, 238, 244)), TextAlignment = TextAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center, ToolTip = TitleBarItems.Label(k)
                    });
                previewPip.Width = Math.Max(240, previewIcons.Children.Count * 30 + 120);   // wide enough for every button and a bit of title
            };

            relayout = () =>
            {
                var on = TitleBarItems.On(App.S.TitleBarButtons);
                var seq = new List<string>(on);
                foreach (var k in TitleBarItems.Keys) if (!seq.Contains(k)) seq.Add(k);
                ex.Rows.Children.Clear();
                redrawPreview();
                ex.Rows.Children.Add(previewRow);
                for (int i = 0; i < seq.Count; i++)
                {
                    string k = seq[i];
                    var row = rows[k];
                    ((Border)row.Children[0]).Visibility = Visibility.Visible;              // the preview sits above the first row
                    ex.Rows.Children.Add(row);
                    int at = on.IndexOf(k);
                    lefts[k].IsEnabled = at > 0;
                    rights[k].IsEnabled = at >= 0 && at < on.Count - 1;
                    switches[k].IsChecked = at >= 0;
                    switches[k].IsEnabled = k != TitleBarItems.Required;
                }
                ex.Rows.Children.Add(resetRow);
            };
            relayout();
        }

        // controls ---------------------------------------------------------------------
        static Wpf.Ui.Controls.ToggleSwitch Switch(Func<bool> get, Action<bool> set)
        {
            var ts = new Wpf.Ui.Controls.ToggleSwitch { IsChecked = get() };
            ts.Click += (o, e) => { set(ts.IsChecked == true); App.I.SettingsChanged(); };
            return ts;
        }

        // where new PiPs open: corners, plus the middle of each edge while 8 spots are on
        static readonly string[] PosLabels = { "Top left", "Top right", "Bottom left", "Bottom right", "Top middle", "Bottom middle", "Left middle", "Right middle" };
        static readonly int[] PosOrder = { 0, 4, 1, 6, 7, 2, 5, 3 };          // shown in reading order
        static ComboBox posCombo;
        static List<int> posValues = new List<int>();
        static bool posSync;

        static ComboBox PositionCombo()
        {
            posCombo = new ComboBox { MinWidth = 200 };
            posCombo.SelectionChanged += (o, e) =>
            {
                if (posSync || posCombo.SelectedIndex < 0) return;
                App.S.DefaultCorner = posValues[posCombo.SelectedIndex];
                App.I.SettingsChanged();
            };
            FillPositions();
            return posCombo;
        }

        /// <summary>Rebuilds the Position choices (the middle spots come and go with "Middle of each edge too").</summary>
        public static void FillPositions()
        {
            if (posCombo == null) return;
            bool edges = App.S.SnapToEdges;
            var vals = PosOrder.Where(v => v < 4 || edges).ToList();
            int cur = App.S.DefaultCorner;
            if (!vals.Contains(cur)) cur = cur == 4 ? 1 : cur == 6 ? 2 : 3;   // a middle spot that's switched off: show its corner
            if (posValues.SequenceEqual(vals) && posCombo.Items.Count == vals.Count && posCombo.SelectedIndex == vals.IndexOf(cur)) return;
            posSync = true;
            posValues = vals;
            posCombo.Items.Clear();
            foreach (var v in vals) posCombo.Items.Add(L.Plain(L.T(PosLabels[v])));
            L.Combo(posCombo);
            posCombo.SelectedIndex = vals.IndexOf(cur);
            posSync = false;
        }

        static ComboBox Combo(string[] options, int value, Action<int> set)
        {
            var cb = new ComboBox { MinWidth = 200 };
            foreach (var o in options) cb.Items.Add(L.Plain(L.T(o)));   // the drop-down list lives in a popup, so translate up front
            L.Combo(cb);
            cb.SelectedIndex = Math.Max(0, Math.Min(options.Length - 1, value));
            cb.SelectionChanged += (o, e) => { if (cb.SelectedIndex >= 0) { set(cb.SelectedIndex); App.I.SettingsChanged(); } };
            return cb;
        }

        static FrameworkElement SliderBox(double min, double max, double value, string suffix, Action<int> set)
        {
            var dp = new DockPanel { Width = 240 };
            var val = Fluent.Label((int)value + suffix, 14, "Sub", false);
            val.Width = 56;
            val.TextAlignment = TextAlignment.Right;
            DockPanel.SetDock(val, Dock.Right);
            dp.Children.Add(val);
            var sl = new Slider { Minimum = min, Maximum = max, Value = value, VerticalAlignment = VerticalAlignment.Center, IsSnapToTickEnabled = true, TickFrequency = 1 };
            dp.Children.Add(sl);
            sl.ValueChanged += (o, e) => { val.Text = (int)sl.Value + suffix; set((int)sl.Value); App.I.SettingsChanged(); };
            return dp;
        }

        static FrameworkElement ColorButton(Func<System.Drawing.Color> get, Action<System.Drawing.Color> set)
        {
            var c0 = get();
            var swatch = new Border { Width = 24, Height = 16, CornerRadius = Looks.CR(3), Background = new SolidColorBrush(Color.FromRgb(c0.R, c0.G, c0.B)), Margin = new Thickness(0, 0, 10, 0), BorderThickness = new Thickness(1) };
            swatch.SetResourceReference(Border.BorderBrushProperty, "CardBorder");
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            sp.Children.Add(swatch);
            sp.Children.Add(new TextBlock { Text = "Choose", VerticalAlignment = VerticalAlignment.Center });
            var b = new Button { Content = sp, MinWidth = 120 };
            b.Click += (o, e) =>
            {
                using (var cd = new System.Windows.Forms.ColorDialog { Color = get(), FullOpen = true })
                {
                    if (cd.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
                    set(cd.Color);
                    swatch.Background = new SolidColorBrush(Color.FromRgb(cd.Color.R, cd.Color.G, cd.Color.B));
                    App.I.SettingsChanged();
                }
            };
            return b;
        }

        static Button PlainButton(string text, Action click, bool accent = false)
        {
            if (accent)
            {
                var ub = new Wpf.Ui.Controls.Button { Content = text, Appearance = Wpf.Ui.Controls.ControlAppearance.Primary, MinWidth = 120 };
                ub.Click += (o, e) => click();
                return ub;
            }
            var b = new Button { Content = text, MinWidth = 120 };
            b.Click += (o, e) => click();
            return b;
        }

        /// <summary>The Phone page: the companion on or off, the address and Pair a phone, then the paired phones with Rename
        /// and Remove.</summary>
        static void BuildPhonePage(StackPanel pp, Settings S)
        {
            if (Packaged.IsPackaged)
            {
                Card(pp, "\uE8EA", "Not in the Microsoft Store version", "The phone companion needs network access the Store version doesn't have. The installer and portable versions from GitHub include it.", null, "phone companion store");
                return;
            }
            Section(pp, "Phone companion");
            Card(pp, "\uE8EA", "Control PiP Studio from your phone", "Manage your PiPs and every app's volume from your phone's browser, over your home Wi-Fi. Windows may ask you to allow PiP Studio on private networks.",
                Switch(() => S.CompanionOn, v => { S.CompanionOn = v; Companion.Apply(); }), "phone mobile android iphone remote companion volume mixer wifi qr");
            var pairBtn = PlainButton("Pair a phone", ShowPairing, true);
            var pairCard = Card(pp, "\uED14", "Pair a phone", "Turn on the switch above first", pairBtn, "pair qr code connect phone scan pin");

            Section(pp, "Paired phones");
            var phonesHost = new StackPanel();
            pp.Children.Add(phonesHost);

            refreshCompanion = () =>
            {
                pairBtn.IsEnabled = Companion.Running;
                string url = Companion.Url;
                if (pairCard.Desc != null)
                    pairCard.Desc.Text = !S.CompanionOn ? "Turn on the switch above first"
                        : Companion.Problem ?? (url == null ? "This PC isn't on a network right now" : "Your phone opens " + url);
                phonesHost.Children.Clear();
                var devices = Companion.Devices();
                if (devices.Count == 0)
                {
                    var none = Fluent.Label("No phones are paired yet. Press Pair a phone above to add one.", 13, "Sub");
                    none.Margin = new Thickness(4, 2, 0, 8);
                    phonesHost.Children.Add(none);
                }
                foreach (var d in devices)
                {
                    var dev = d;
                    string seen = dev.LastSeen == null ? "Not used since PiP Studio started" : "Last used " + dev.LastSeen.Value.ToString("t");
                    var buttons = new StackPanel { Orientation = Orientation.Horizontal };
                    var rename = PlainButton("Rename", () => RenamePhone(dev));
                    rename.Margin = new Thickness(0, 0, 8, 0);
                    var remove = PlainButton("Remove", () =>
                        Ask("Remove " + dev.Name + "?", "It stops working right away. You can pair it again any time.", "Remove", "Cancel", ok => { if (ok) Companion.Remove(dev.Id); }));
                    buttons.Children.Add(rename); buttons.Children.Add(remove);
                    Card(phonesHost, "\uE8EA", dev.Name, seen, buttons, "paired phone device rename remove");
                }
            };
            refreshCompanion();
        }

        /// <summary>Rename a paired phone (its name comes from what the phone's browser says it is, like "Pixel 7").</summary>
        static void RenamePhone(Companion.Device dev)
        {
            var box = new TextBox { Text = dev.Name, MaxLength = 40, MinWidth = 280, Padding = new Thickness(8, 6, 8, 6) };
            box.Loaded += (o, e) => box.SelectAll();
            Ask("Rename phone", "The name shows here, so you can tell your phones apart.", "Save", "Cancel", ok =>
            {
                string name = (box.Text ?? "").Trim();
                if (ok && name.Length > 0) Companion.Rename(dev.Id, name);
            }, false, box, box);
        }

        /// <summary>The Pair a phone dialog, like Moonlight and Sunshine: the phone shows a 6-digit PIN and you type it here.
        /// The QR code only carries the page's address; phones can start pairing only while this dialog is open.</summary>
        static void ShowPairing()
        {
            string url = Companion.Url;
            if (url == null)
            {
                Ask("No network", "PiP Studio can't find this PC's address on your local network. Connect to Wi-Fi or Ethernet and try again.", "OK", null, null);
                return;
            }
            Companion.BeginPairing();
            var panel = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 0), MinWidth = 300 };
            try
            {
                using (var gen = new QRCoder.QRCodeGenerator())
                using (var data = gen.CreateQrCode(url, QRCoder.QRCodeGenerator.ECCLevel.M))
                {
                    byte[] png = new QRCoder.PngByteQRCode(data).GetGraphic(8);   // black on white, with a quiet border
                    var img = new BitmapImage();
                    img.BeginInit(); img.StreamSource = new MemoryStream(png); img.CacheOption = BitmapCacheOption.OnLoad; img.EndInit(); img.Freeze();
                    var frame = new Border { Background = Brushes.White, CornerRadius = Looks.CR(8), Padding = new Thickness(6), HorizontalAlignment = HorizontalAlignment.Center,
                        Child = new Image { Source = img, Width = 180, Height = 180, SnapsToDevicePixels = true } };
                    RenderOptions.SetBitmapScalingMode(frame.Child, BitmapScalingMode.NearestNeighbor);   // crisp squares
                    panel.Children.Add(frame);
                }
            }
            catch (Exception ex) { App.ReportError(ex); }
            var addr = Fluent.Label("Or open " + url + " on your phone", 13, "Sub");
            addr.TextAlignment = TextAlignment.Center;
            addr.Margin = new Thickness(0, 10, 0, 14);
            panel.Children.Add(addr);

            var pinRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
            var pinBox = new TextBox { Width = 150, FontSize = 22, MaxLength = 7, TextAlignment = TextAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center, Padding = new Thickness(6, 4, 6, 4) };
            var pairBtn = PlainButton("Pair", () => { }, true);
            pairBtn.Margin = new Thickness(8, 0, 0, 0);
            pinRow.Children.Add(pinBox); pinRow.Children.Add(pairBtn);
            panel.Children.Add(pinRow);
            var status = Fluent.Label("Waiting for your phone…", 13, "Sub");
            status.TextAlignment = TextAlignment.Center;
            status.Margin = new Thickness(0, 10, 0, 0);
            panel.Children.Add(status);

            bool done = false;
            Action tryPin = () =>
            {
                if (done) return;
                string name;
                switch (Companion.ConfirmPin(pinBox.Text, out name))
                {
                    case Companion.PinResult.Paired:
                        done = true;
                        status.Text = "Paired with " + name + ". You can close this.";
                        status.SetResourceReference(TextBlock.ForegroundProperty, "Accent");
                        pinBox.IsEnabled = pairBtn.IsEnabled = false;
                        break;
                    case Companion.PinResult.Wrong: status.Text = "That PIN doesn't match. Check the PIN on your phone."; pinBox.SelectAll(); break;
                    case Companion.PinResult.TooMany: status.Text = "Too many wrong PINs. Start pairing again on your phone."; pinBox.Clear(); break;
                    default: status.Text = "No phone is showing a PIN yet. Open the page on your phone and tap Pair."; break;
                }
            };
            pairBtn.Click += (o, e) => tryPin();
            pinBox.KeyDown += (o, e) => { if (e.Key == Key.Enter) { tryPin(); e.Handled = true; } };
            Action onChange = () =>
            {
                if (!done && Companion.PhoneWaiting) status.Text = "Your phone is showing a PIN. Type it here.";
            };
            Companion.Changed += onChange;
            Ask("Pair a phone",
                "1. Scan this code with your phone's camera (it needs to be on the same Wi-Fi as this PC) and tap Pair.\n2. Your phone shows a 6-digit PIN. Type it below.",
                "Close", null, ok => { Companion.Changed -= onChange; Companion.EndPairing(); }, false, panel, pinBox);
        }

        /// <summary>Settings > General > Updates: where things stand, Check now / Update now / What's new, and the daily check.</summary>
        static void BuildUpdateSettings(StackPanel ge, Settings S)
        {
            Section(ge, "Updates");
            var buttons = new StackPanel { Orientation = Orientation.Horizontal };
            var whatsNew = PlainButton("What's new", Updater.OpenReleasePage);
            var check = PlainButton("Check now", () => Updater.Check(true));
            var install = PlainButton("Update now", Updater.Install, true);
            whatsNew.Margin = new Thickness(0, 0, 8, 0);
            check.Margin = new Thickness(0, 0, 8, 0);
            buttons.Children.Add(whatsNew); buttons.Children.Add(check); buttons.Children.Add(install);
            var card = Card(ge, "", "Updates", Updater.Status, buttons, "update upgrade new version download latest");
            Card(ge, "", "Check for updates automatically", "Once a day, on GitHub. Nothing about you or your PC is sent.", Switch(() => S.CheckForUpdates, v => S.CheckForUpdates = v), "update automatic check github");

            refreshUpdates = () =>
            {
                if (card.Desc != null) card.Desc.Text = Updater.Status;
                bool busy = Updater.Busy;
                install.Visibility = Updater.CanInstall ? Visibility.Visible : Visibility.Collapsed;
                check.Visibility = Updater.CanInstall ? Visibility.Collapsed : Visibility.Visible;
                whatsNew.Visibility = Updater.Latest != null ? Visibility.Visible : Visibility.Collapsed;
                check.IsEnabled = install.IsEnabled = !busy;
            };
            refreshUpdates();
        }

        // ----------------------------------------------------------------------------- the settings pages
        static void BuildSettingsPages()
        {
            var S = App.S;
            string[] spots = { "Top left", "Top right", "Bottom left", "Bottom right" };

            // ---------------- Appearance
            var ap = NewPage("appearance");
            Section(ap, "Colors");
            Card(ap, "", "App theme", "Light or dark, for PiP Studio's windows and menus",
                Combo(new[] { "Use my Windows setting", "Light", "Dark" }, S.ThemeMode, v => S.ThemeMode = v), "light dark mode night");
            Card(ap, "\uE771", "Window style", "How this window looks. PiPs and menus keep their look.",
                Combo(Looks.Names, Looks.Current, v =>
                {
                    if (v == Looks.Current) return;
                    S.WindowStyle = v;
                    App.I.SettingsChanged();
                    w.Dispatcher.BeginInvoke(new Action(SwitchLanguage), DispatcherPriority.Background);   // rebuilds the window in the new look
                }), "windows 11 xbox console green look skin style theme dark");
            Button colorBtn = null;
            var accent = MakeExpander(ap, "", "Accent color", "Borders, highlights and controller hints", null, false, "color colour highlight");
            Row(accent, "Use my Windows accent color", null, Switch(() => S.UseSystemAccent, v => { S.UseSystemAccent = v; if (colorBtn != null) colorBtn.IsEnabled = !v; }), "system automatic");
            colorBtn = (Button)ColorButton(() => S.AccentColor, v => S.AccentColor = v);
            colorBtn.IsEnabled = !S.UseSystemAccent;
            Row(accent, "Custom accent color", "Used when the Windows accent color is off", colorBtn, "custom pick");

            Section(ap, "PiP window");
            Card(ap, "", "Rounded corners", "Windows 11 only", Switch(() => S.RoundedCorners, v => S.RoundedCorners = v), "round radius corner");
            var border = MakeExpander(ap, "", "Border", "A thin outline around each PiP, in your accent color or one you pick", Switch(() => S.ShowBorder, v => S.ShowBorder = v), false, "outline frame edge");
            Row(border, "Width", null, SliderBox(1, 8, S.BorderWidth, " px", v => S.BorderWidth = v), "thickness");
            var borderColorBtn = (Button)ColorButton(() => S.BorderColor, v => S.BorderColor = v);
            borderColorBtn.IsEnabled = !S.BorderUseAccent;
            Row(border, "Match accent color", null, Switch(() => S.BorderUseAccent, v => { S.BorderUseAccent = v; borderColorBtn.IsEnabled = !v; }), "accent theme");
            Row(border, "Border color", "Used when Match accent color is off", borderColorBtn, "colour outline pick letterbox");

            Card(ap, "\uE8C1", "Messages under the PiP", "When you switch sources or change a mode, the message shows below the PiP instead of over the picture", Switch(() => S.OsdBelow, v => S.OsdBelow = v), "caption osd title name label below toast");

            BuildTitleBarSettings(ap);

            Section(ap, "Effects");
            Card(ap, "\uE945", "Lite mode", "For slower PCs: turns off animations, the Mica background, shadows and live previews",
                Switch(() => S.LiteMode, v => { S.LiteMode = v; ApplyLite(); }), "performance fast low end slow lag light simple battery");
            Card(ap, "", "Animation effects", "Glide, fade and slide effects in PiP Studio and on PiPs", Switch(() => S.Animations, v => S.Animations = v), "motion animate smooth");

            // ---------------- Behavior
            var be = NewPage("behavior");
            Section(be, "New PiPs");
            var np = MakeExpander(be, "", "Where and how they open", null, null, true, "default new");
            Row(np, "Position", null, PositionCombo(), "corner start middle edge top bottom left right");
            Row(np, "Size", null, Combo(SizePresets.Names, S.DefaultSize, v => S.DefaultSize = v), "big small");
            Row(np, "Opacity", null, SliderBox(10, 100, S.DefaultOpacityPercent, "%", v => S.DefaultOpacityPercent = v), "transparency");
            var lim = MakeExpander(be, "\uE740", "Size limits", "Keep tall or wide apps from getting too big. Applies to new PiPs, size presets, resizing and source changes (theater mode ignores them).", null, false, "advanced maximum max width height tall huge big limit cap");
            Row(lim, "Limit width", null, Switch(() => S.LimitWidth, v => S.LimitWidth = v), "maximum width wide");
            Row(lim, "Maximum width", "Share of the screen", SliderBox(10, 95, S.MaxWidthPercent, "%", v => S.MaxWidthPercent = v), "maximum width wide percent");
            Row(lim, "Limit height", null, Switch(() => S.LimitHeight, v => S.LimitHeight = v), "maximum height tall");
            Row(lim, "Maximum height", "Share of the screen", SliderBox(10, 95, S.MaxHeightPercent, "%", v => S.MaxHeightPercent = v), "maximum height tall percent");
            Card(be, "\uE8A7", "Control other apps' PiP windows", "When no PiP Studio PiP is open, your shortcuts and controller move, resize, fade, mute and close the picture-in-picture window of Chrome, Edge, Firefox and other apps.",
                Switch(() => S.ControlOtherPips, v => S.ControlOtherPips = v), "chrome firefox edge browser native video picture in picture external other");
            Card(be, "\uE71E", "Find other apps' PiPs automatically", "Small always-on-top windows, like video players' mini modes, count as PiPs. Mark one as Not a PiP on the Running PiPs page.",
                Switch(() => S.DetectOtherPips, v => S.DetectOtherPips = v), "detect find automatic floating mini player always on top video telegram zoom spotify vlc");
            var marksBtn = new Button { Content = "Reset", Padding = new Thickness(14, 4, 14, 4) };
            marksBtn.Click += (o, e) => { ExternalPip.ClearMarks(); runSig = ""; winSig = ""; };
            Card(be, "\uE894", "Windows you marked", "Forget every Treat as a PiP and Not a PiP you chose", marksBtn, "mark treat not pip reset forget list");
            Card(be, "\uE7C9", "Show controls on other apps' PiPs", "Point at another app's PiP to get PiP Studio's title bar over it: drag it to move the PiP, scroll on it to change the volume, and use the title bar buttons you picked.",
                Switch(() => S.HoverOtherPips, v => S.HoverOtherPips = v), "hover controls title bar buttons point mouse chrome firefox discord external other");
            Card(be, "\uE8B0", "Snap other apps' PiPs to spots", "Other apps' PiP windows land on the same corners and edge spots as PiP Studio's PiPs, when they open and when you let go of them after dragging.",
                Switch(() => S.SnapOtherPips, v => S.SnapOtherPips = v), "snap corners spots chrome firefox discord external other");
            Card(be, "\uE737", "Allow more than one PiP at a time", "When off, opening a new PiP switches the one that's already open",
                Switch(() => S.AllowMultiple, v => S.AllowMultiple = v), "multiple several many limit one single");
            Card(be, "", "Follow the window I just left", "When you switch windows, the PiP switches to the one you were on",
                Switch(() => S.FollowPreviousDefault, v => S.FollowPreviousDefault = v), "auto switch previous last active alt tab");

            Section(be, "Placement");
            var snap = MakeExpander(be, "", "Snap after moving", "Glides to the nearest spot when you let go", Switch(() => S.SnapToCorners, v => S.SnapToCorners = v), false, "snap corner magnet");
            Row(snap, "Middle of each edge too", "8 spots instead of 4 corners — also used by the arrow keys and D-pad", Switch(() => S.SnapToEdges, v => S.SnapToEdges = v), "edges spots positions");
            Row(snap, "Gap from the screen edges", null, SliderBox(0, 120, S.EdgeMargin, " px", v => S.EdgeMargin = v), "margin distance");
            Card(be, "", "Stay on one display", "Moving past the edge won't hop to the next display", Switch(() => S.LockToMonitor, v => S.LockToMonitor = v), "monitor lock screen hop");

            Section(be, "Mouse");
            var hover = MakeExpander(be, "", "When the pointer is over a PiP", "Move out of the way slides it aside — hold Ctrl to grab it instead",
                Combo(new[] { "Show controls", "Fade out", "Do nothing", "Move out of the way" }, S.HoverAction, v => S.HoverAction = v), false, "hover mouse dodge fade controls");
            Row(hover, "Show move arrows", "Arrow buttons send the PiP to the next spot — or the next display", Switch(() => S.ShowMoveArrows, v => S.ShowMoveArrows = v), "arrows throw move");
            Row(hover, "Fade-out opacity", "How visible a PiP stays while it fades under the pointer", SliderBox(0, 100, S.HoverFadePercent, "%", v => S.HoverFadePercent = v), "transparent");
            Row(hover, "Click-through opacity", "How visible a ghost-mode PiP is under the pointer", SliderBox(0, 100, S.ClickThroughHoverPercent, "%", v => S.ClickThroughHoverPercent = v), "ghost transparent");
            Card(be, "", "Right-click on a PiP", null, Combo(new[] { "Show the menu", "Close it", "Bring its source to the front", "Theater mode", "Do nothing" }, S.RightClickAction, v => S.RightClickAction = v), "context menu right click");
            Card(be, "", "Middle-click closes a PiP", null, Switch(() => S.MiddleClickCloses, v => S.MiddleClickCloses = v), "wheel click close");

            Section(be, "Smart hiding");
            Card(be, "", "Hide a PiP while its window is in front", "It would only repeat what's already on screen", Switch(() => S.HideWhenSourceActive, v => S.HideWhenSourceActive = v), "auto hide active focused duplicate");
            Card(be, "", "Hide PiPs while a fullscreen app is on the same display", "Borderless games still show PiPs unless this is on", Switch(() => S.HideInFullscreen, v => S.HideInFullscreen = v), "game fullscreen presentation");

            Section(be, "Sources");
            Card(be, "", "Reconnect when a closed app opens again", null, Switch(() => S.AutoReattach, v => S.AutoReattach = v), "reattach restore reopen");
            Card(be, "", "Keep minimized sources playing", "Minimizing a source tucks it off-screen so the PiP keeps moving. Click it in the taskbar to bring it back.", Switch(() => S.KeepAliveMinimized, v => S.KeepAliveMinimized = v), "minimize park hide offscreen");
            var cap = MakeExpander(be, "", "Screen regions", "Window sources always run at full speed", null, false, "fps capture region display");
            Row(cap, "Frame rate", null, SliderBox(5, 60, S.CaptureFps, " fps", v => S.CaptureFps = v), "fps smooth");
            Row(cap, "Show the pointer", null, Switch(() => S.CaptureCursor, v => S.CaptureCursor = v), "cursor mouse");

            // ---------------- Controller
            var pc = NewPage("controller");
            var padSwitch = new Wpf.Ui.Controls.ToggleSwitch();
            padSwitch.Click += (o, e) => App.I.SetPadOn(padSwitch.IsChecked == true);
            var status = Card(pc, "", "Controller controls", " ", padSwitch, "gamepad xbox playstation switch joystick");
            Card(pc, "\uE7FC", "Controller-friendly mode", "Bigger layout, a focus ring and button hints, so you can use this window with a controller. Pressing a key on the keyboard switches back.",
                Combo(new[] { "Off", "When I use a controller", "Always" }, S.PadUiMode, v => { S.PadUiMode = v; Dispatch(() => SetPadVisual(v == 2)); }), "gamepad console big picture tv couch navigate ui");
            Card(pc, "\uE7B3", "Fade controller hints when idle", "The PiP's name and button hints fade after a few seconds without input. Any button brings them back. Less for Windows to draw over your game.",
                Switch(() => S.PadHintsAutoHide, v => S.PadHintsAutoHide = v), "hints fade hide idle performance fps");
            Section(pc, "Options");
            var opts = MakeExpander(pc, "", "Controller options", null, null, false, "gamepad options");
            Row(opts, "Use game controllers", "Xbox, PlayStation, Switch and most others", Switch(() => S.PadEnabled, v => S.PadEnabled = v), "enable");
            Row(opts, "Turn controls on when PiP Studio starts", null, Switch(() => S.PadStartOn, v => S.PadStartOn = v), "startup");
            Row(opts, "Release the game's focus while controls are on", "The game ignores the controller while you move the PiP, and gets focus back afterwards", Switch(() => S.PadFocusSink, v => S.PadFocusSink = v), "focus game sink");
            Row(opts, "Show button hints on the PiP", null, Switch(() => S.PadHints, v => S.PadHints = v), "hints help chips");
            Section(pc, "Buttons");
            var resetPad = new Button { Content = "Reset" };
            var binds = MakeExpander(pc, "", "Button assignments", "Click a binding, then press the button or combination. Right-click clears it. Trigger: press, double press or hold.", resetPad, true, "binding buttons");
            var padButtons = new Dictionary<string, Button>();
            var modeButtons = new Dictionary<string, Button>();
            string[] modeNames = { "Press", "Double press", "Hold" };
            foreach (var a in App.I.Actions)
            {
                var act = a;
                var pb = new Button { MinWidth = 150, Margin = new Thickness(0, 0, 8, 0), ToolTip = "Click, then press the button(s). Right-click to clear." };
                var mb = new Button { MinWidth = 112, ToolTip = "Press once, press twice quickly, or hold" };
                var ctl = new StackPanel { Orientation = Orientation.Horizontal };
                ctl.Children.Add(pb);
                ctl.Children.Add(mb);
                padButtons[act.Key] = pb;
                modeButtons[act.Key] = mb;
                pb.Click += (o, e) =>
                {
                    if (!App.I.PadConnected) { pb.Content = "Connect a controller"; DelayRun(1500, () => { if (refreshBindings != null) refreshBindings(); }); return; }
                    pb.Content = "Press button(s)…";
                    App.I.CapturePad(mask => { App.I.PadMask[act.Key] = mask; App.I.SavePadBindings(); if (refreshBindings != null) refreshBindings(); });
                };
                pb.MouseRightButtonUp += (o, e) => { App.I.PadMask[act.Key] = 0; App.I.SavePadBindings(); if (refreshBindings != null) refreshBindings(); };
                mb.Click += (o, e) => { App.I.PadMode[act.Key] = (App.I.PadMode[act.Key] + 1) % 3; App.I.SavePadBindings(); if (refreshBindings != null) refreshBindings(); };
                Row(binds, act.Label, null, ctl, "controller button gamepad binding " + act.Brief);
            }
            resetPad.Click += (o, e) => { App.I.ResetPadBindings(); if (refreshBindings != null) refreshBindings(); };
            refreshBindings = () =>
            {
                var A = App.I;
                foreach (var kv in padButtons) kv.Value.Content = Pad.Name(A.PadMask[kv.Key], A.PadKind);
                foreach (var kv in modeButtons) kv.Value.Content = modeNames[A.PadMode[kv.Key]];
                padSwitch.IsChecked = A.PadOn;
                padSwitch.IsEnabled = A.PadConnected;
                status.Title.Text = A.PadConnected ? Pad.FamilyName(A.PadKind) + " connected" : "No controller connected";
                status.Desc.Text = A.PadConnected
                    ? (A.PadOn ? "Controls are on — the PiP has an accent outline. " + Pad.Name(A.PadMask["padoff"], A.PadKind) + " turns them off."
                               : "Press " + Pad.Name(A.PadMask["pad"], A.PadKind) + " to turn controls on.")
                    : "Connect an Xbox, PlayStation or Switch controller by USB or Bluetooth.";
            };
            refreshBindings();

            // ---------------- Shortcuts (grouped, like Windows Settings)
            var sc = NewPage("shortcuts");
            Note(sc, "These work anywhere in Windows. Click a shortcut, then press the new keys (Backspace clears, Esc cancels). Right-click clears it. They act on the PiP you used or pointed at most recently.");
            var groups = new[]
            {
                Tuple.Create("", "Open and choose", new[] { "showapp", "newpip", "pipactive", "region", "prevsrc", "nextsrc", "follow" }),
                Tuple.Create("", "Move and size", new[] { "up", "down", "left", "right", "corner", "nextmon", "grow", "shrink", "theater", "dodge" }),
                Tuple.Create("", "Look and visibility", new[] { "opup", "opdown", "toggleall", "clickthrough", "crop" }),
                Tuple.Create("", "Source and media", new[] { "startpip", "swap", "mute", "volup", "voldown", "playpause", "pad", "close" }),
            };
            var keyButtons = new List<Tuple<string, Button>>();
            bool firstGroup = true;
            foreach (var grp in groups)
            {
                var ex = MakeExpander(sc, grp.Item1, grp.Item2, null, null, firstGroup, "shortcut hotkey keyboard");
                firstGroup = false;
                foreach (var key in grp.Item3)
                {
                    var act = App.I.Actions.FirstOrDefault(x => x.Key == key);
                    if (act == null || act.HkField == null) continue;
                    var kb = new Button { MinWidth = 190, Content = KeyText(act.HkField), ToolTip = "Click, then press keys. Right-click to clear." };
                    string field = act.HkField;
                    kb.Click += (o, e) => BeginKeyCapture(field, kb);
                    kb.MouseRightButtonUp += (o, e) => { HK.Set(App.S, field, ""); kb.Content = KeyText(field); App.I.SettingsChanged(true); };
                    keyButtons.Add(Tuple.Create(field, kb));
                    Row(ex, act.Label, null, kb, "shortcut hotkey keyboard key " + act.Brief);
                }
            }
            Card(sc, "", "Reset shortcuts", "Puts every shortcut back to its default", PlainButton("Reset", () =>
            {
                var d = new Settings();
                foreach (var f in typeof(Settings).GetFields().Where(f => f.Name.StartsWith("HK_"))) f.SetValue(App.S, f.GetValue(d));
                foreach (var kb in keyButtons) kb.Item2.Content = KeyText(kb.Item1);
                App.I.SettingsChanged(true);
            }), "default restore");

            // ---------------- Settings (general)
            var ge = NewPage("settings");
            Section(ge, "Language");
            Card(ge, "\uE774", "Language", "Switches right away",
                Combo(L.Names, Math.Max(0, Array.IndexOf(L.Codes, S.Language ?? "")), v =>
                {
                    if (L.Codes[v] == (S.Language ?? "")) return;
                    S.Language = L.Codes[v];
                    w.Dispatcher.BeginInvoke(new Action(SwitchLanguage), DispatcherPriority.Background);
                }), "language arabic chinese english translate عربي 中文 语言 اللغة");
            Section(ge, "Startup");
            Card(ge, "", "Start PiP Studio when I sign in", "It starts quietly in the tray", Switch(() => App.StartupEnabled, v => { try { App.StartupEnabled = v; } catch { } }), "boot login windows startup");
            Card(ge, "", "Bring back my PiPs when PiP Studio starts", null, Switch(() => S.RestoreSessions, v => S.RestoreSessions = v), "restore session remember layout");
            Card(ge, "", "Notifications", "Tips and alerts from the tray icon", Switch(() => S.ShowTips, v => S.ShowTips = v), "balloon toast notification tips");
            Section(ge, "Privacy");
            Card(ge, "", "Hide PiPs from screenshots, recordings and screen sharing", "OBS, Discord, Teams and the Snipping Tool won't see them. Costs a little performance, so leave it off unless you stream or share your screen.", Switch(() => S.HideFromCapture, v => S.HideFromCapture = v), "capture stream share record obs");
            Section(ge, "Performance");
            Card(ge, "", "Keep running in the tray when I close this window", "Otherwise closing this window quits PiP Studio", Switch(() => S.CloseToTray, v => S.CloseToTray = v), "tray background close quit");
            Card(ge, "", "Release memory while idle", "Gives unused memory back to Windows while PiP Studio sits in the tray", Switch(() => S.MinimizeMemory, v => S.MinimizeMemory = v), "ram memory trim");
            if (!Packaged.IsPackaged)                                     // the Store version can't restart itself elevated
            {
                Section(ge, "Administrator");
                if (App.IsElevated) Card(ge, "", "Running as administrator", "PiP Studio can show windows of apps that run as administrator", null, "admin elevated uac");
                else Card(ge, "", "Restart as administrator", "Needed only to show windows of apps that run as administrator", PlainButton("Restart", App.I.RestartElevated), "admin elevated uac");
                var always = new Wpf.Ui.Controls.ToggleSwitch { IsChecked = S.AlwaysAdmin && AdminStart.TaskExists() };
                always.Click += (o, e) =>
                {
                    bool want = always.IsChecked == true;
                    bool ok = want ? AdminStart.Create() : AdminStart.Delete();   // Windows asks for permission either way
                    if (!ok) { always.IsChecked = !want; return; }               // you said no: nothing changed
                    S.AlwaysAdmin = want;
                    App.I.SettingsChanged();
                    if (want && !App.IsElevated)
                        Ask("Restart as administrator now?", "From now on PiP Studio starts as administrator without asking, also when it starts at sign-in.",
                            "Restart now", "Later", yes => { if (yes) AdminStart.RestartThroughTask(); });
                };
                Card(ge, "\uE7EF", "Always start as administrator", "Without asking each time, also at sign-in. For games and apps that run as administrator: PiP Studio can then show their windows and take their focus for controller controls. Windows asks once to set it up.", always, "admin elevated uac always startup sign in task");
            }
            if (Updater.Supported) BuildUpdateSettings(ge, S);   // the Store version gets its updates from the Store
            Section(ge, "About");
            var about = MakeExpander(ge, "", "PiP Studio", "Version " + Updater.CurrentText, PlainButton("Exit PiP Studio", App.I.ExitApp), false, "version about exit quit");
            Row(about, "Settings folder", Packaged.RealPath(Settings.Dir), PlainButton("Open", () => { try { Directory.CreateDirectory(Settings.Dir); Process.Start("explorer.exe", "\"" + Packaged.RealPath(Settings.Dir) + "\""); } catch { } }), "folder files log");
            Row(about, "Report a problem", "Opens a bug report on GitHub with your PiP Studio and Windows versions and recent errors filled in", PlainButton("Report", CrashReports.ReportProblem), "bug report problem crash issue feedback github");
            Row(about, "Picture-in-picture for any window or part of the screen", "Live window mirroring uses Windows' own thumbnail system, so it costs almost nothing to run.", null, "about");

            BuildPhonePage(NewPage("phone"), S);                          // the phone companion has its own page
            NewPage("search");
        }
    }
}
