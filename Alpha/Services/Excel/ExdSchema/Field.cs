using System.ComponentModel;
using YamlDotNet.Serialization;

namespace Alpha.Services.Excel.ExdSchema;

[YamlSerializable]
public class Field {
    [YamlMember(Order = 0)] public string? Name { get; set; }

    public string? PendingName { get; set; }

    [DefaultValue(FieldType.Scalar)] public FieldType Type { get; set; }

    public int? Count { get; set; }

    public string? Comment { get; set; }

    public List<Field>? Fields { get; set; }

    public Dictionary<string, List<string>>? Relations { get; set; }

    public Condition? Condition { get; set; }

    public List<string>? Targets { get; set; }

    public override string ToString() {
        return $"{this.Name} ({this.Type})";
    }
}

[YamlSerializable]
public enum FieldType {
    Scalar,
    Link,
    Array,
    Icon,
    ModelId,
    Color
}
