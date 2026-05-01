using System;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using MacroKeyboard.Core.Models;

namespace MacroKeyboard.UI.ViewModels;

/// <summary>
/// ViewModel для одного шага в последовательности действий
/// </summary>
public class SequenceStepViewModel : ViewModelBase
{
    public SequenceStepViewModel()
    {
        // Initialize available action types (excluding Sequence to prevent recursion)
        AvailableActionTypes = new ObservableCollection<ActionType>
        {
            ActionType.Keyboard,
            ActionType.CustomHid,
            ActionType.ProfileSwitch,
            ActionType.Folder,
            ActionType.Delay,
            ActionType.Shell
        };
        
        // Initialize available folder IDs (0-15)
        AvailableFolderIds = new ObservableCollection<byte>();
        for (byte i = 0; i <= 15; i++)
        {
            AvailableFolderIds.Add(i);
        }
        
        // Initialize available profile IDs (0-7)
        AvailableProfileIds = new ObservableCollection<byte>();
        for (byte i = 0; i <= 7; i++)
        {
            AvailableProfileIds.Add(i);
        }
    }
    
    /// <summary>
    /// Available action types for sequence steps (excludes Sequence to prevent recursion)
    /// </summary>
    public ObservableCollection<ActionType> AvailableActionTypes { get; }
    
    /// <summary>
    /// Available folder IDs (0-15)
    /// </summary>
    public ObservableCollection<byte> AvailableFolderIds { get; }
    
    /// <summary>
    /// Available profile IDs (0-7)
    /// </summary>
    public ObservableCollection<byte> AvailableProfileIds { get; }
    
    private int _stepNumber;
    public int StepNumber
    {
        get => _stepNumber;
        set => SetProperty(ref _stepNumber, value);
    }
    
    private ActionType _actionType = ActionType.Keyboard;
    
    /// <summary>
    /// Action type for this step (alias for SelectedActionType for XAML binding)
    /// </summary>
    public ActionType ActionType
    {
        get => _actionType;
        set
        {
            if (SetProperty(ref _actionType, value))
            {
                OnPropertyChanged(nameof(SelectedActionType));
                OnPropertyChanged(nameof(IsKeyboardStep));
                OnPropertyChanged(nameof(IsShellStep));
                OnPropertyChanged(nameof(IsCustomHidStep));
                OnPropertyChanged(nameof(IsProfileSwitchStep));
                OnPropertyChanged(nameof(IsFolderStep));
                OnPropertyChanged(nameof(IsDelayStep));
            }
        }
    }
    
    /// <summary>
    /// Alias for ActionType for backward compatibility
    /// </summary>
    public ActionType SelectedActionType
    {
        get => _actionType;
        set => ActionType = value;
    }
    
    private ushort _delayBeforeMs;
    public ushort DelayBeforeMs
    {
        get => _delayBeforeMs;
        set => SetProperty(ref _delayBeforeMs, value);
    }
    
    // ============================================
    // Keyboard action properties
    // ============================================
    
    private string _keyboardText = string.Empty;
    public string KeyboardText
    {
        get => _keyboardText;
        set => SetProperty(ref _keyboardText, value);
    }
    
    private byte _keyCode;
    public byte KeyCode
    {
        get => _keyCode;
        set => SetProperty(ref _keyCode, value);
    }
    
    private KeyModifiers _modifiers = KeyModifiers.None;
    public KeyModifiers Modifiers
    {
        get => _modifiers;
        set => SetProperty(ref _modifiers, value);
    }
    
    // ============================================
    // Shell action properties
    // ============================================
    
    private string _shellCommand = string.Empty;
    public string ShellCommand
    {
        get => _shellCommand;
        set => SetProperty(ref _shellCommand, value);
    }
    
    private string? _workingDirectory;
    public string? WorkingDirectory
    {
        get => _workingDirectory;
        set => SetProperty(ref _workingDirectory, value);
    }
    
    private bool _waitForExit = true;
    public bool WaitForExit
    {
        get => _waitForExit;
        set => SetProperty(ref _waitForExit, value);
    }
    
    private int _shellTimeoutMs = 30000;
    public int ShellTimeoutMs
    {
        get => _shellTimeoutMs;
        set => SetProperty(ref _shellTimeoutMs, value);
    }
    
    // ============================================
    // CustomHID action properties
    // ============================================
    
    private string _customHidData = string.Empty;
    public string CustomHidData
    {
        get => _customHidData;
        set => SetProperty(ref _customHidData, value);
    }
    
    // ============================================
    // ProfileSwitch action properties
    // ============================================
    
    private byte _targetProfileId;
    public byte TargetProfileId
    {
        get => _targetProfileId;
        set => SetProperty(ref _targetProfileId, value);
    }
    
    // ============================================
    // Folder action properties
    // ============================================
    
    private byte _folderId;
    public byte FolderId
    {
        get => _folderId;
        set => SetProperty(ref _folderId, value);
    }
    
    // ============================================
    // Delay action properties
    // ============================================
    
    private ushort _delayMs;
    public ushort DelayMs
    {
        get => _delayMs;
        set => SetProperty(ref _delayMs, value);
    }
    
    // ============================================
    // Visibility helpers
    // ============================================
    
    public bool IsKeyboardStep => SelectedActionType == ActionType.Keyboard;
    public bool IsShellStep => SelectedActionType == ActionType.Shell;
    public bool IsCustomHidStep => SelectedActionType == ActionType.CustomHid;
    public bool IsProfileSwitchStep => SelectedActionType == ActionType.ProfileSwitch;
    public bool IsFolderStep => SelectedActionType == ActionType.Folder;
    public bool IsDelayStep => SelectedActionType == ActionType.Delay;
    
    /// <summary>
    /// Конвертировать ViewModel в модель SequenceStep
    /// </summary>
    public SequenceStep ToModel()
    {
        ActionConfig action = SelectedActionType switch
        {
            ActionType.Keyboard => new KeyboardAction
            {
                KeyCode = KeyCode,
                Modifiers = Modifiers,
                Text = string.IsNullOrEmpty(KeyboardText) ? null : KeyboardText
            },
            ActionType.Shell => new ShellAction
            {
                Command = ShellCommand,
                WorkingDirectory = WorkingDirectory,
                WaitForExit = WaitForExit,
                TimeoutMs = ShellTimeoutMs
            },
            ActionType.CustomHid => new CustomHidAction
            {
                Data = ParseHexString(CustomHidData)
            },
            ActionType.ProfileSwitch => new ProfileSwitchAction
            {
                TargetProfileId = TargetProfileId
            },
            ActionType.Folder => new FolderAction
            {
                FolderId = FolderId
            },
            ActionType.Delay => new DelayAction
            {
                DelayMs = DelayMs
            },
            _ => new KeyboardAction()
        };
        
        return new SequenceStep
        {
            Action = action,
            DelayBeforeMs = DelayBeforeMs
        };
    }
    
    /// <summary>
    /// Загрузить данные из модели SequenceStep
    /// </summary>
    public void LoadFromModel(SequenceStep step)
    {
        SelectedActionType = step.Action.ActionType;
        DelayBeforeMs = step.DelayBeforeMs;
        
        switch (step.Action)
        {
            case KeyboardAction keyboard:
                KeyCode = keyboard.KeyCode;
                Modifiers = keyboard.Modifiers;
                KeyboardText = keyboard.Text ?? string.Empty;
                break;
                
            case ShellAction shell:
                ShellCommand = shell.Command;
                WorkingDirectory = shell.WorkingDirectory;
                WaitForExit = shell.WaitForExit;
                ShellTimeoutMs = shell.TimeoutMs;
                break;
                
            case CustomHidAction customHid:
                CustomHidData = BitConverter.ToString(customHid.Data).Replace("-", " ");
                break;
                
            case ProfileSwitchAction profileSwitch:
                TargetProfileId = profileSwitch.TargetProfileId;
                break;
                
            case FolderAction folder:
                FolderId = folder.FolderId;
                break;
                
            case DelayAction delay:
                DelayMs = delay.DelayMs;
                break;
        }
    }
    
    /// <summary>
    /// Парсинг hex-строки в массив байтов
    /// </summary>
    private static byte[] ParseHexString(string hex)
    {
        if (string.IsNullOrWhiteSpace(hex))
            return Array.Empty<byte>();
            
        // Remove spaces and common separators
        hex = hex.Replace(" ", "").Replace("-", "").Replace(":", "");
        
        if (hex.Length % 2 != 0)
            return Array.Empty<byte>();
            
        try
        {
            var bytes = new byte[hex.Length / 2];
            for (int i = 0; i < bytes.Length; i++)
            {
                bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
            }
            return bytes;
        }
        catch
        {
            return Array.Empty<byte>();
        }
    }
}
