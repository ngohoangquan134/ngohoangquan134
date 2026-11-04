namespace KetCauViet.Core;

public sealed class TypeSelectionFailure : Exception
{
    public string Code { get; }
    public TypeSelectionFailure(string code, string message) : base(message) => Code = code;
}

// Plain immutable data only. No Revit Element, Document or ElementId wrapper.
public sealed class TypeChoice
{
    public Guid ScopeId { get; }
    public int ElementId { get; }
    public string UniqueId { get; }
    public string Revision { get; }
    public string Name { get; }
    public double NominalMm { get; }
    public double ModelMm { get; }
    public double StandardBendMm { get; }
    public double TieBendMm { get; }
    public bool MatchesNominal(double diameterMm) => Engineering.Finite(diameterMm) && Math.Abs(NominalMm - diameterMm) <= 1e-6;
    internal TypeChoice(Guid scope, TypeStateData data)
    {
        ScopeId = scope; ElementId = data.ElementId; UniqueId = data.UniqueId; Revision = data.Revision; Name = data.Name;
        NominalMm = data.NominalMm; ModelMm = data.ModelMm; StandardBendMm = data.StandardBendMm; TieBendMm = data.TieBendMm;
    }
}

public sealed class TypeStateData
{
    public int ElementId { get; }
    public string UniqueId { get; }
    public string Revision { get; }
    public string Name { get; }
    public double NominalMm { get; }
    public double ModelMm { get; }
    public double StandardBendMm { get; }
    public double TieBendMm { get; }
    public TypeStateData(int elementId, string uniqueId, string revision, string name, double nominalMm, double modelMm, double standardBendMm, double tieBendMm)
    {
        ElementId = elementId; UniqueId = uniqueId; Revision = revision; Name = name; NominalMm = nominalMm; ModelMm = modelMm;
        StandardBendMm = standardBendMm; TieBendMm = tieBendMm;
    }
}

public sealed class LiveType<T> where T : class
{
    public T? Value { get; }
    public TypeStateData Data { get; }
    public bool IsValid { get; }
    public bool IsBarType { get; }
    public bool OwnerMatches { get; }
    public LiveType(T? value, TypeStateData data, bool valid = true, bool barType = true, bool ownerMatches = true)
    { Value = value; Data = data; IsValid = valid; IsBarType = barType; OwnerMatches = ownerMatches; }
}

// Shared production policy, tested without pretending to execute Revit.
// The adapter supplies native Document.Equals and a fresh Document.GetElement on every read.
public sealed class TypeSelectionScope<T> : IDisposable where T : class
{
    public Guid Id { get; } = Guid.NewGuid();
    private readonly Func<bool> isCurrent;
    private readonly Func<int, LiveType<T>?> read;
    private bool disposed;
    public TypeSelectionScope(Func<bool> isCurrent, Func<int, LiveType<T>?> read) { this.isCurrent = isCurrent; this.read = read; }

    public void EnsureCurrent()
    {
        if (disposed) throw new TypeSelectionFailure("SCOPE_CLOSED", "Lựa chọn thuộc lần chạy đã kết thúc. Chạy lại lệnh để chọn type.");
        if (!isCurrent()) throw new TypeSelectionFailure("DOCUMENT_CHANGED", "Document hiện tại đã đổi hoặc đóng. Chạy lại lệnh trên dự án cần bố trí thép.");
    }

    private LiveType<T> ReadValid(int id)
    {
        EnsureCurrent();
        var state = read(id);
        if (state == null) throw new TypeSelectionFailure("TYPE_DELETED", "Type ID " + id + " không còn tồn tại. Mở lại hộp thoại và chọn type.");
        if (!state.IsValid) throw new TypeSelectionFailure("INVALID_OBJECT", "Type ID " + id + " đã mất hiệu lực. Chọn lại type từ dự án hiện tại.");
        if (!state.IsBarType) throw new TypeSelectionFailure("WRONG_CLASS", "Element ID " + id + " không phải RebarBarType.");
        if (!state.OwnerMatches) throw new TypeSelectionFailure("FOREIGN_DOCUMENT", "Type ID " + id + " thuộc Document khác.");
        if (state.Value == null) throw new TypeSelectionFailure("INVALID_OBJECT", "Không truy xuất được đối tượng type hiện tại.");
        if (state.Data.ElementId != id || string.IsNullOrWhiteSpace(state.Data.UniqueId)) throw new TypeSelectionFailure("INVALID_ID", "Không xác minh được định danh type ID " + id + ".");
        var data = state.Data;
        if (!Positive(data.NominalMm) || !Positive(data.ModelMm)) throw new TypeSelectionFailure("INVALID_DIAMETER", "Type ID " + id + " có đường kính danh nghĩa hoặc mô hình không hợp lệ.");
        return state;
    }
    private static bool Positive(double n) => Engineering.Finite(n) && n > 0;

    public TypeChoice Capture(int id) => new(Id, ReadValid(id).Data);

    public T Resolve(TypeChoice choice, bool stirrup = false)
    {
        EnsureCurrent();
        if (choice == null || choice.ScopeId != Id) throw new TypeSelectionFailure("FOREIGN_SCOPE", "Lựa chọn không thuộc lần chạy/Document hiện tại. Chọn lại type.");
        var state = ReadValid(choice.ElementId); var data = state.Data;
        if (data.UniqueId != choice.UniqueId) throw new TypeSelectionFailure("REPLACED_ID", "ElementId " + choice.ElementId + " đã trỏ tới phần tử khác. Không thay thế type theo ID hoặc tên.");
        if (data.Revision != choice.Revision || data.Name != choice.Name || data.NominalMm != choice.NominalMm || data.ModelMm != choice.ModelMm || data.StandardBendMm != choice.StandardBendMm || data.TieBendMm != choice.TieBendMm)
            throw new TypeSelectionFailure("TYPE_CHANGED", "Type " + choice.Name + " (ID " + choice.ElementId + ") đã thay đổi sau khi chọn. Mở lại hộp thoại để kiểm tra và chọn lại. Snapshot=" + choice.Revision + "; current=" + data.Revision + ".");
        var bend = stirrup ? data.TieBendMm : data.StandardBendMm;
        if (!Positive(bend) || bend < data.ModelMm) throw new TypeSelectionFailure("INVALID_BEND", "Type " + data.Name + " có đường kính uốn " + (stirrup ? "đai" : "thép dọc/phân bố") + " không hợp lệ.");
        return state.Value!;
    }

    public IReadOnlyList<string> Preflight(IEnumerable<(string Role, TypeChoice Choice, bool Stirrup)> selections)
    {
        var failures = new List<string>();
        foreach (var item in selections)
        {
            try { Resolve(item.Choice, item.Stirrup); }
            catch (TypeSelectionFailure ex) { failures.Add(item.Role + " [" + ex.Code + "]: " + ex.Message); }
        }
        return failures;
    }
    public void Dispose() => disposed = true;
}
