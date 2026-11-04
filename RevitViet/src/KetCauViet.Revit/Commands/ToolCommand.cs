using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using KetCauViet.Revit.Infrastructure;
using KetCauViet.Revit.Tools;

namespace KetCauViet.Revit.Commands;

public abstract class ToolCommand : IExternalCommand
{
    protected abstract string Tool { get; }
    public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
    {
        try
        {
            if (Tool == "Help") { Documentation.Show(); return Result.Succeeded; }
            if (data.Application.ActiveUIDocument == null || data.Application.ActiveUIDocument.Document.IsFamilyDocument)
            { TaskDialog.Show("Kết Cấu Việt", "Hãy mở một mô hình dự án Revit trước khi dùng công cụ."); return Result.Cancelled; }
            var context = new ToolContext(data.Application);
            switch (Tool)
            {
                case "AutoJoin": StructuralTools.AutoJoin(context); break;
                case "CadModel": StructuralTools.CadModel(context); break;
                case "ChangeFramingType": StructuralTools.ChangeFramingType(context); break;
                case "PileCoordinates": StructuralTools.PileCoordinates(context); break;
                case "AutoNumber": StructuralTools.AutoNumber(context); break;
                case "LiningConcrete": StructuralTools.LiningConcrete(context); break;
                case "ToggleJoins": StructuralTools.ToggleJoins(context); break;
                case "CutByFace": StructuralTools.CutByFace(context); break;
                case "StructuralUtilities": StructuralTools.StructuralUtilities(context); break;
                case "NumberRebar": RebarTools.NumberRebar(context); break;
                case "ScheduleRebar": RebarTools.ScheduleRebar(context); break;
                case "BeamRebar": RebarTools.BeamRebar(context); break;
                case "ColumnWallRebar": RebarTools.ColumnWallRebar(context); break;
                case "FloorRebar": RebarTools.FloorRebar(context); break;
                case "RebarUtilities": RebarTools.RebarUtilities(context); break;
                case "LoadFamily": DrawingTools.LoadFamily(context); break;
                case "BeamDrawing": DrawingTools.BeamDrawing(context); break;
                case "Rebar2D": DrawingTools.Rebar2D(context); break;
                case "AutoDimension": DrawingTools.AutoDimension(context); break;
                case "FormworkArea": QuantityTools.FormworkArea(context); break;
                case "ConcreteVolume": QuantityTools.ConcreteVolume(context); break;
                case "CoreWallVolume": QuantityTools.CoreWallVolume(context); break;
                case "PickFace": QuantityTools.PickFace(context); break;
                case "SumParameter": QuantityTools.SumParameter(context); break;
                case "RebarWeight": QuantityTools.RebarWeight(context); break;
                case "Waterproofing": QuantityTools.Waterproofing(context); break;
                default: throw new InvalidOperationException("Lệnh không hợp lệ: " + Tool);
            }
            return Result.Succeeded;
        }
        catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
        catch (System.OperationCanceledException) { return Result.Cancelled; }
        catch (Exception ex)
        {
            message = ex.Message;
            TaskDialog.Show("Kết Cấu Việt · Không hoàn tất", ex.Message);
            return Result.Failed;
        }
    }
}
[Transaction(TransactionMode.Manual)]
public sealed class AutoJoinCommand : ToolCommand { protected override string Tool => "AutoJoin"; }
[Transaction(TransactionMode.Manual)]
public sealed class CadModelCommand : ToolCommand { protected override string Tool => "CadModel"; }
[Transaction(TransactionMode.Manual)]
public sealed class ChangeFramingTypeCommand : ToolCommand { protected override string Tool => "ChangeFramingType"; }
[Transaction(TransactionMode.Manual)]
public sealed class PileCoordinatesCommand : ToolCommand { protected override string Tool => "PileCoordinates"; }
[Transaction(TransactionMode.Manual)]
public sealed class AutoNumberCommand : ToolCommand { protected override string Tool => "AutoNumber"; }
[Transaction(TransactionMode.Manual)]
public sealed class LiningConcreteCommand : ToolCommand { protected override string Tool => "LiningConcrete"; }
[Transaction(TransactionMode.Manual)]
public sealed class ToggleJoinsCommand : ToolCommand { protected override string Tool => "ToggleJoins"; }
[Transaction(TransactionMode.Manual)]
public sealed class CutByFaceCommand : ToolCommand { protected override string Tool => "CutByFace"; }
[Transaction(TransactionMode.Manual)]
public sealed class StructuralUtilitiesCommand : ToolCommand { protected override string Tool => "StructuralUtilities"; }
[Transaction(TransactionMode.Manual)]
public sealed class NumberRebarCommand : ToolCommand { protected override string Tool => "NumberRebar"; }
[Transaction(TransactionMode.Manual)]
public sealed class ScheduleRebarCommand : ToolCommand { protected override string Tool => "ScheduleRebar"; }
[Transaction(TransactionMode.Manual)]
public sealed class BeamRebarCommand : ToolCommand { protected override string Tool => "BeamRebar"; }
[Transaction(TransactionMode.Manual)]
public sealed class ColumnWallRebarCommand : ToolCommand { protected override string Tool => "ColumnWallRebar"; }
[Transaction(TransactionMode.Manual)]
public sealed class FloorRebarCommand : ToolCommand { protected override string Tool => "FloorRebar"; }
[Transaction(TransactionMode.Manual)]
public sealed class RebarUtilitiesCommand : ToolCommand { protected override string Tool => "RebarUtilities"; }
[Transaction(TransactionMode.Manual)]
public sealed class LoadFamilyCommand : ToolCommand { protected override string Tool => "LoadFamily"; }
[Transaction(TransactionMode.Manual)]
public sealed class BeamDrawingCommand : ToolCommand { protected override string Tool => "BeamDrawing"; }
[Transaction(TransactionMode.Manual)]
public sealed class Rebar2DCommand : ToolCommand { protected override string Tool => "Rebar2D"; }
[Transaction(TransactionMode.Manual)]
public sealed class AutoDimensionCommand : ToolCommand { protected override string Tool => "AutoDimension"; }
[Transaction(TransactionMode.Manual)]
public sealed class FormworkAreaCommand : ToolCommand { protected override string Tool => "FormworkArea"; }
[Transaction(TransactionMode.Manual)]
public sealed class ConcreteVolumeCommand : ToolCommand { protected override string Tool => "ConcreteVolume"; }
[Transaction(TransactionMode.Manual)]
public sealed class CoreWallVolumeCommand : ToolCommand { protected override string Tool => "CoreWallVolume"; }
[Transaction(TransactionMode.Manual)]
public sealed class PickFaceCommand : ToolCommand { protected override string Tool => "PickFace"; }
[Transaction(TransactionMode.Manual)]
public sealed class SumParameterCommand : ToolCommand { protected override string Tool => "SumParameter"; }
[Transaction(TransactionMode.Manual)]
public sealed class RebarWeightCommand : ToolCommand { protected override string Tool => "RebarWeight"; }
[Transaction(TransactionMode.Manual)]
public sealed class WaterproofingCommand : ToolCommand { protected override string Tool => "Waterproofing"; }
[Transaction(TransactionMode.Manual)]
public sealed class HelpCommand : ToolCommand { protected override string Tool => "Help"; }
