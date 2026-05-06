using Microsoft.Maui.Graphics;

namespace NCAA_Power_Ratings.Mobile.Controls;

public partial class FollowIcon : ContentView
{
    public FollowIcon()
    {
        InitializeComponent();
    }

    // FOLLOW STATE

    public static readonly BindableProperty IsFollowedProperty =
        BindableProperty.Create(
            nameof(IsFollowed),
            typeof(bool),
            typeof(FollowIcon),
            false);

    public bool IsFollowed
    {
        get => (bool)GetValue(IsFollowedProperty);
        set => SetValue(IsFollowedProperty, value);
    }

    // GLYPH ON

    public static readonly BindableProperty GlyphOnProperty =
        BindableProperty.Create(
            nameof(GlyphOn),
            typeof(string),
            typeof(FollowIcon),
            "★");

    public string GlyphOn
    {
        get => (string)GetValue(GlyphOnProperty);
        set => SetValue(GlyphOnProperty, value);
    }

    // GLYPH OFF

    public static readonly BindableProperty GlyphOffProperty =
        BindableProperty.Create(
            nameof(GlyphOff),
            typeof(string),
            typeof(FollowIcon),
            "☆");

    public string GlyphOff
    {
        get => (string)GetValue(GlyphOffProperty);
        set => SetValue(GlyphOffProperty, value);
    }

    // COLOR ON

    public static readonly BindableProperty ColorOnProperty =
        BindableProperty.Create(
            nameof(ColorOn),
            typeof(Color),
            typeof(FollowIcon),
            Colors.Gold);

    public Color ColorOn
    {
        get => (Color)GetValue(ColorOnProperty);
        set => SetValue(ColorOnProperty, value);
    }

    // COLOR OFF

    public static readonly BindableProperty ColorOffProperty =
        BindableProperty.Create(
            nameof(ColorOff),
            typeof(Color),
            typeof(FollowIcon),
            Colors.Gray);

    public Color ColorOff
    {
        get => (Color)GetValue(ColorOffProperty);
        set => SetValue(ColorOffProperty, value);
    }

    // ICON SIZE

    public static readonly BindableProperty IconSizeProperty =
        BindableProperty.Create(
            nameof(IconSize),
            typeof(double),
            typeof(FollowIcon),
            16.0);

    public double IconSize
    {
        get => (double)GetValue(IconSizeProperty);
        set => SetValue(IconSizeProperty, value);
    }
}