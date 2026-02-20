using DiskpartGUI.ViewModels;

namespace DiskpartGUI.Services;

public interface IDialogService
{
    bool? ShowAddPartitionDialog(AddPartitionViewModel vm);
    bool? ShowDeletePartitionDialog(DeletePartitionViewModel vm);
    bool? ShowResizePartitionDialog(ResizePartitionViewModel vm);
    bool? ShowMovePartitionDialog(MovePartitionViewModel vm);
    void ShowAboutDialog();
    void ShowError(string title, string message);
}
