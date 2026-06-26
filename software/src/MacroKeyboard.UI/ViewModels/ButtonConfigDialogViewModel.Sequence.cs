using CommunityToolkit.Mvvm.Input;
using MacroKeyboard.Core.Models;
using Microsoft.Extensions.Logging;
using System.Collections.ObjectModel;

namespace MacroKeyboard.UI.ViewModels;

public partial class ButtonConfigDialogViewModel
{
    // ── Sequence ──────────────────────────────────────────────────────────────

    public ObservableCollection<SequenceStepViewModel> SequenceSteps { get; } = new();

    [RelayCommand]
    private void AddSequenceStep()
    {
        if (SequenceSteps.Count < SequenceAction.MaxSteps)
        {
            var step = new SequenceStepViewModel
            {
                StepNumber = SequenceSteps.Count + 1,
                SelectedActionType = ActionType.Keyboard,
                DelayBeforeMs = 0
            };
            SequenceSteps.Add(step);
            OnPropertyChanged(nameof(CanAddMoreSteps));
            _logger.LogDebug("Added sequence step {StepNumber}", step.StepNumber);
        }
    }

    [RelayCommand]
    private void RemoveSequenceStep(SequenceStepViewModel? step)
    {
        if (step != null && SequenceSteps.Contains(step))
        {
            SequenceSteps.Remove(step);
            for (int i = 0; i < SequenceSteps.Count; i++)
                SequenceSteps[i].StepNumber = i + 1;
            OnPropertyChanged(nameof(CanAddMoreSteps));
            _logger.LogDebug("Removed sequence step, {Count} steps remaining", SequenceSteps.Count);
        }
    }

    [RelayCommand]
    private void MoveStepUp(SequenceStepViewModel? step)
    {
        if (step == null) return;
        var index = SequenceSteps.IndexOf(step);
        if (index > 0)
        {
            SequenceSteps.Move(index, index - 1);
            for (int i = 0; i < SequenceSteps.Count; i++)
                SequenceSteps[i].StepNumber = i + 1;
        }
    }

    [RelayCommand]
    private void MoveStepDown(SequenceStepViewModel? step)
    {
        if (step == null) return;
        var index = SequenceSteps.IndexOf(step);
        if (index >= 0 && index < SequenceSteps.Count - 1)
        {
            SequenceSteps.Move(index, index + 1);
            for (int i = 0; i < SequenceSteps.Count; i++)
                SequenceSteps[i].StepNumber = i + 1;
        }
    }
}
