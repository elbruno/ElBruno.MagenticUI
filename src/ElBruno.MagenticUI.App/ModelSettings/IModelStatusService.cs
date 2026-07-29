namespace ElBruno.MagenticUI.App.ModelSettings;

public interface IModelStatusService
{
    IReadOnlyList<ModelStatusSnapshot> GetStatuses();
}
