namespace QuireStudio.Presentation.ViewModels.Components;

/// <summary>
/// 下拉选项：<see cref="Value"/> 是要写回配置的取值，<see cref="Label"/> 是展示给用户的文案。
/// <see cref="ToString"/> 返回文案，因此 ComboBox 默认就能显示人类可读的名字。
/// </summary>
public sealed record ChoiceOption(string Value, string Label)
{
    public override string ToString() => Label;
}
