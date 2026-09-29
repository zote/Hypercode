"""Gera hypercode/Styles/FluentOverrides.axaml a partir das chaves do FluentTheme.

Uso: gen_overrides.py <FluentControlResources.xaml> <saída>

O primeiro argumento é src/Avalonia.Themes.Fluent/Accents/FluentControlResources.xaml, no
repositório do Avalonia, na tag da versão em uso (regenerar.sh baixa esse arquivo). Cada
chave do dicionário Default passa pelas RULES: a primeira que casar dá o token, e chave que
não casa com nenhuma continua com o valor do Fluent. Ver tools/README.md.
"""
import re
import sys

if len(sys.argv) != 3:
    sys.exit(__doc__)
FLUENT, OUT = sys.argv[1:3]

src = open(FLUENT, encoding="utf-8").read()
default = src[: src.index('x:Key="Dark"')]
keys = [k for t, k in re.findall(r'<(\w+) x:Key="(\w+)"', default) if t in ("StaticResource", "SolidColorBrush")]
# O Fluent não define o estado normal de Checked na série CheckBoxCheckBackgroundStroke (o
# template usa o Fill); a chave entra para a série ficar completa.
keys.append("CheckBoxCheckBackgroundStrokeChecked")

ACT = r"(PointerOver|Pressed|SelectedPointerOver|SelectedPressed)"
CHK = r"(Checked|Indeterminate)"

# (regex, token) — a primeira regra que casar vence; token None = mantém o do Fluent.
RULES = [
    # Botão de destaque
    (r"AccentButtonBackgroundDisabled$", "Brush.Background.Control"),
    (r"AccentButtonBackgroundPressed$", "Brush.Accent.Pressed"),
    (r"AccentButtonBackground", "Brush.Selection.Active"),
    (r"AccentButtonForegroundDisabled$", "Brush.Label.Tertiary"),
    (r"AccentButtonForeground", "Brush.Text.OnAccent"),
    (r"AccentButtonBorderBrushDisabled$", "Brush.Border.Control"),
    (r"AccentButtonBorderBrushPressed$", "Brush.Accent.Pressed"),
    (r"AccentButtonBorderBrush", "Brush.Selection.Active"),
    # Botão comum e RepeatButton (setas do NumericUpDown)
    (r"(Repeat)?ButtonBackgroundPressed$", "Brush.Background.ControlPressed"),
    (r"(Repeat)?ButtonBackground", "Brush.Background.Control"),
    (r"(Repeat)?ButtonForegroundDisabled$", "Brush.Label.Tertiary"),
    (r"(Repeat)?ButtonForeground", "Brush.Label.Primary"),
    (r"(Repeat)?ButtonBorderBrushDisabled$", "Brush.Separator"),
    (r"(Repeat)?ButtonBorderBrush", "Brush.Border.Control"),
    # Campo de texto
    (r"TextControlForegroundDisabled$", "Brush.Label.Tertiary"),
    (r"TextControlForeground", "Brush.Label.Primary"),
    (r"TextControlBackground", "Brush.Background.Content"),
    (r"TextControlBorderBrushFocused$", "Brush.Accent"),
    (r"TextControlBorderBrushDisabled$", "Brush.Separator"),
    (r"TextControlBorderBrush", "Brush.Border.Control"),
    (r"TextControlPlaceholderForegroundDisabled$", "Brush.Label.Tertiary"),
    (r"TextControlPlaceholderForeground", "Brush.Label.Secondary"),
    (r"TextControl", None),
    # Checkbox
    (r"CheckBoxForeground\w*Disabled$", "Brush.Label.Tertiary"),
    (r"CheckBoxForeground", "Brush.Label.Primary"),
    (r"CheckBoxCheckBackgroundStroke\w*Disabled$", "Brush.Separator"),
    (r"CheckBoxCheckBackgroundStrokeUnchecked", "Brush.Border.Control"),
    (rf"CheckBoxCheckBackgroundStroke{CHK}Pressed$", "Brush.Accent.Pressed"),
    (rf"CheckBoxCheckBackgroundStroke{CHK}", "Brush.Selection.Active"),
    (r"CheckBoxCheckBackgroundFillUncheckedPressed$", "Brush.Background.ControlPressed"),
    (r"CheckBoxCheckBackgroundFillUnchecked", "Brush.Background.Control"),
    (rf"CheckBoxCheckBackgroundFill{CHK}Disabled$", "Brush.Background.Control"),
    (rf"CheckBoxCheckBackgroundFill{CHK}Pressed$", "Brush.Accent.Pressed"),
    (rf"CheckBoxCheckBackgroundFill{CHK}", "Brush.Selection.Active"),
    (rf"CheckBoxCheckGlyphForeground{CHK}Disabled$", "Brush.Label.Tertiary"),
    (rf"CheckBoxCheckGlyphForeground{CHK}", "Brush.Text.OnAccent"),
    (r"CheckBox", None),
    # Radio
    (r"RadioButtonForegroundDisabled$", "Brush.Label.Tertiary"),
    (r"RadioButtonForeground", "Brush.Label.Primary"),
    (r"RadioButtonOuterEllipseStrokeDisabled$", "Brush.Separator"),
    (r"RadioButtonOuterEllipseStroke", "Brush.Border.Control"),
    (r"RadioButtonOuterEllipseFillPressed$", "Brush.Background.ControlPressed"),
    (r"RadioButtonOuterEllipseFill", "Brush.Background.Control"),
    (r"RadioButtonOuterEllipseCheckedStrokeDisabled$", "Brush.Separator"),
    (r"RadioButtonOuterEllipseCheckedStrokePressed$", "Brush.Accent.Pressed"),
    (r"RadioButtonOuterEllipseCheckedStroke", "Brush.Selection.Active"),
    (r"RadioButtonOuterEllipseCheckedFillDisabled$", "Brush.Background.Control"),
    (r"RadioButtonOuterEllipseCheckedFillPressed$", "Brush.Accent.Pressed"),
    (r"RadioButtonOuterEllipseCheckedFill", "Brush.Selection.Active"),
    (r"RadioButtonCheckGlyphFillDisabled$", "Brush.Label.Tertiary"),
    (r"RadioButtonCheckGlyphFill", "Brush.Text.OnAccent"),
    (r"RadioButton", None),
    # Combo (pop-up button) e a lista dele
    (rf"ComboBoxItemForeground{ACT}$", "Brush.Text.OnAccent"),
    (r"ComboBoxItemForeground\w*Disabled$", "Brush.Label.Tertiary"),
    (r"ComboBoxItemForeground", "Brush.Label.Primary"),
    (rf"ComboBoxItemBackground{ACT}$", "Brush.Selection.Active"),
    (r"ComboBoxItemBackgroundSelected$", "Brush.Selection.Inactive"),
    (r"ComboBoxItem", None),
    (r"ComboBoxBackgroundPressed$", "Brush.Background.ControlPressed"),
    (r"ComboBoxBackground(PointerOver|Disabled)?$", "Brush.Background.Control"),
    (r"ComboBoxForegroundDisabled$", "Brush.Label.Tertiary"),
    (r"ComboBoxForeground", "Brush.Label.Primary"),
    (r"ComboBoxPlaceHolderForeground", "Brush.Label.Secondary"),
    (r"ComboBoxBorderBrushDisabled$", "Brush.Separator"),
    (r"ComboBoxBorderBrush", "Brush.Border.Control"),
    (r"ComboBoxDropDownGlyphForegroundDisabled$", "Brush.Label.Tertiary"),
    (r"ComboBoxDropDownGlyphForeground", "Brush.Label.Secondary"),
    (r"ComboBoxDropDownBackground$", "Brush.Background.Overlay"),
    (r"ComboBoxDropDownBorderBrush$", "Brush.Border.Overlay"),
    (r"ComboBox", None),
    # Menu (de contexto e flyout)
    (r"MenuFlyoutItemBackground(PointerOver|Pressed)$", "Brush.Selection.Active"),
    (r"MenuFlyoutItemBackground", None),
    (r"MenuFlyoutItemForeground(PointerOver|Pressed)$", "Brush.Text.OnAccent"),
    (r"MenuFlyoutItemForegroundDisabled$", "Brush.Label.Tertiary"),
    (r"MenuFlyoutItemForeground$", "Brush.Label.Primary"),
    (r"MenuFlyoutSubItemChevron(PointerOver|Pressed|SubMenuOpened)$", "Brush.Text.OnAccent"),
    (r"MenuFlyoutSubItemChevronDisabled$", "Brush.Label.Tertiary"),
    (r"MenuFlyoutSubItemChevron$", "Brush.Label.Secondary"),
    (r"MenuFlyoutItemKeyboardAcceleratorTextForeground(PointerOver|Pressed)$", "Brush.Text.OnAccent"),
    (r"MenuFlyoutItemKeyboardAcceleratorTextForegroundDisabled$", "Brush.Label.Tertiary"),
    (r"MenuFlyoutItemKeyboardAcceleratorTextForeground$", "Brush.Label.Secondary"),
    (r"MenuFlyoutPresenterBackground$", "Brush.Background.Overlay"),
    (r"MenuFlyoutPresenterBorderBrush$", "Brush.Border.Overlay"),
    # Tooltip
    (r"ToolTipForeground$", "Brush.Label.Primary"),
    (r"ToolTipBackground$", "Brush.Background.Overlay"),
    (r"ToolTipBorderBrush$", "Brush.Border.Overlay"),
]

GROUPS = [
    ("Botão de destaque (Classes=\"accent\" e botão padrão)", "AccentButton"),
    ("Botão comum", "Button"),
    ("Setas do NumericUpDown", "RepeatButton"),
    ("Campo de texto (TextBox, NumericUpDown)", "TextControl"),
    ("Checkbox", "CheckBox"),
    ("Radio", "RadioButton"),
    ("Combo e a lista dele", "ComboBox"),
    ("Menu de contexto e flyout", "MenuFlyout"),
    ("Tooltip", "ToolTip"),
]

mapping = {}
for k in keys:
    for pattern, token in RULES:
        if re.match(pattern, k):
            if token:
                mapping[k] = token
            break


def group_of(k):
    for title, prefix in GROUPS:
        if k.startswith(prefix) and not (prefix == "Button" and k.startswith(("ButtonSpinner",))):
            return title
    return None


def section(indent):
    lines = []
    for title, prefix in GROUPS:
        items = [k for k in mapping if group_of(k) == title]
        if not items:
            continue
        lines.append(f"{indent}<!-- {title} -->")
        for k in items:
            lines.append(f'{indent}<StaticResource x:Key="{k}" ResourceKey="{mapping[k]}" />')
        lines.append("")
    return "\n".join(lines).rstrip() + "\n"


body = f'''<!--
  Liga as chaves de recurso do FluentTheme 11.3 aos tokens de Styles/Tokens.axaml, para os
  controles do Fluent (botão, campo, checkbox, radio, combo, menu, tooltip) usarem as cores,
  a fonte e os raios do design system sem reescrever os templates. Estados e variantes que o
  Fluent não tem (lista estilo macOS, botão destrutivo, anel de foco…) ficam em
  Styles/Controls.axaml. Uso: Styles/README.md.

  As duas variantes têm exatamente os mesmos aliases: um StaticResource num dicionário de tema
  só enxerga os tokens da mesma variante, por isso a lista se repete (o teste
  FluentOverridesTests garante que Light e Dark não divergem). Chave do Fluent que não
  aparece aqui continua com o valor dele.

  Gerado por Styles/tools/gen_overrides.py: para mudar um alias, mude a regra lá e gere de
  novo (Styles/tools/README.md), em vez de editar este arquivo à mão.
-->
<ResourceDictionary xmlns="https://github.com/avaloniaui"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">

  <!-- Tamanho do texto dos controles: Body do macOS (o Fluent usa 14) -->
  <StaticResource x:Key="ControlContentThemeFontSize" ResourceKey="Font.Size.Body" />
  <StaticResource x:Key="ControlCornerRadius" ResourceKey="Radius.Control" />
  <StaticResource x:Key="OverlayCornerRadius" ResourceKey="Radius.Group" />

  <!-- Medidas do AppKit no macOS 27 (#97): botão e campo com 24pt de altura e 12pt de padding
       lateral no botão; menu com 5pt em cima e embaixo, itens de 24pt com o texto a 14pt da
       borda do menu e separador de 11pt com a linha recuada 16pt (1 de borda + 15) -->
  <Thickness x:Key="ButtonPadding">12,3</Thickness>
  <Thickness x:Key="TextControlThemePadding">6,3</Thickness>
  <Thickness x:Key="MenuFlyoutPresenterThemePadding">0,5</Thickness>
  <Thickness x:Key="MenuFlyoutItemThemePaddingNarrow">8,4</Thickness>
  <Thickness x:Key="MenuFlyoutSeparatorThemePadding">15,5</Thickness>
  <x:Double x:Key="TextControlPlaceholderOpacity">1</x:Double>
  <StaticResource x:Key="ToolTipContentThemeFontSize" ResourceKey="Font.Size.Subheadline" />
  <Thickness x:Key="ToolTipBorderThemePadding">6,3</Thickness>

  <ResourceDictionary.ThemeDictionaries>

    <ResourceDictionary x:Key="Light">
{section("      ")}    </ResourceDictionary>

    <ResourceDictionary x:Key="Dark">
{section("      ")}    </ResourceDictionary>

  </ResourceDictionary.ThemeDictionaries>

</ResourceDictionary>
'''
open(OUT, "w", encoding="utf-8").write(body)
print(f"{len(mapping)} chaves mapeadas")
