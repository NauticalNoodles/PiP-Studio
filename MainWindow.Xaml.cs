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
    // The window's XAML (parsed at run time) and the styles it uses. Part of MainUI (MainWindow.cs).
    static partial class MainUI
    {
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
    <!-- touch: drag a finger to scroll every list and page (WPF leaves touch panning off) -->
    <Style TargetType='ScrollViewer' BasedOn='{StaticResource {x:Type ScrollViewer}}'><Setter Property='PanningMode' Value='VerticalFirst'/></Style>
    <Style TargetType='ListBox' BasedOn='{StaticResource {x:Type ListBox}}'><Setter Property='ScrollViewer.PanningMode' Value='VerticalFirst'/></Style>
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

          <!-- Settings pages: an animated explainer for the setting you're pointing at. A small PiP in the lower right corner;
               in controller mode a panel on the right (ExplainLayout switches between the two) -->
          <Border x:Name='ExplainCard' Panel.ZIndex='10' Grid.Row='1' Grid.RowSpan='2' Width='316' Margin='0,22,28,24' HorizontalAlignment='Right' VerticalAlignment='Top' Visibility='Collapsed' Padding='16,14,16,16' CornerRadius='8' BorderThickness='1' Background='{DynamicResource Card}' BorderBrush='{DynamicResource CardBorder}'>
            <StackPanel x:Name='ExplainBody'>
              <Grid>
                <Border x:Name='ExplainFrame' Height='178' CornerRadius='4' Background='#FF15161A' ClipToBounds='True'>
                  <Viewbox x:Name='ExplainView' Stretch='Uniform' Margin='6'>
                    <Canvas x:Name='ExplainStage' Width='264' Height='150' ClipToBounds='True'/>
                  </Viewbox>
                </Border>
                <Button x:Name='ExplainX' Visibility='Collapsed' Opacity='0' HorizontalAlignment='Right' VerticalAlignment='Top' Margin='0,6,6,0' Width='26' Height='26' Padding='0' Content='&#xE711;' FontFamily='Segoe Fluent Icons, Segoe MDL2 Assets' FontSize='10' ToolTip='Hide the explanations (you can bring them back from the top of the page)'/>
              </Grid>
              <TextBlock x:Name='ExplainTitle' Margin='0,14,0,0' FontSize='16' FontWeight='SemiBold' Foreground='{DynamicResource Text}' TextWrapping='Wrap'/>
              <TextBlock x:Name='ExplainText' Margin='0,4,0,0' Style='{StaticResource Caption}' FontSize='13' TextWrapping='Wrap'/>
              <DockPanel x:Name='ExplainFoot' Margin='0,12,0,0'>
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
                <ListBox x:Name='WinList' Background='Transparent' BorderThickness='0' Padding='0,0,14,0' ItemTemplate='{StaticResource WinRow}' ItemContainerStyle='{StaticResource RowItem}' HorizontalContentAlignment='Stretch' ScrollViewer.HorizontalScrollBarVisibility='Disabled' VirtualizingPanel.IsVirtualizing='True' VirtualizingPanel.ScrollUnit='Pixel'/>
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
                <ListBox x:Name='RunList' Background='Transparent' BorderThickness='0' Padding='0,0,14,0' ItemTemplate='{StaticResource RunRow}' ItemContainerStyle='{StaticResource RowItem}' HorizontalContentAlignment='Stretch' ScrollViewer.HorizontalScrollBarVisibility='Disabled' VirtualizingPanel.ScrollUnit='Pixel'/>
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
    }
}
