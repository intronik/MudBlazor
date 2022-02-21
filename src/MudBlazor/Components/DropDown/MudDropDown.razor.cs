// Copyright (c) MudBlazor 2021
// MudBlazor licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.Web.Virtualization;
using MudBlazor.Services;
using MudBlazor.Utilities;
using MudBlazor.Utilities.Exceptions;

namespace MudBlazor
{
    public partial class MudDropDown<T> : MudBaseInput<T>, IDisposable
    {
        readonly string _elementId = $"_{Guid.NewGuid().ToString()[0..8]}";
        readonly string _listId = $"_{Guid.NewGuid().ToString()[0..8]}";
        private HashSet<T> _selectedValues = new();
        private IEqualityComparer<T> _comparer;
        private bool? _selectAllChecked;
        private int? _cachedCount;
        private bool _isOpen;
        private object _activeItemId;
        private string _multiSelectionText;
        private Func<T, string> _toStringFunc = x => x?.ToString();
        private MudInput<string> _elementReference;
        [Inject] private IKeyInterceptor _keyInterceptor { get; set; }
        [Inject] IScrollManager ScrollManager { get; set; }
        protected string Classname =>
            new CssBuilder("mud-dropdown")
            .AddClass(Class)
            .Build()
        ;
        private ValueTask<ItemsProviderResult<(T Item, int Index)>> LoadItems(ItemsProviderRequest request)
        {
            _cachedCount ??= Items.Count();
            var result = new ItemsProviderResult<(T Item, int Index)>(
                  items: Items.Skip(request.StartIndex).Take(request.Count).Select((item, index) => (item, index + request.StartIndex))
                , totalItemCount: _cachedCount.Value
            );
            return ValueTask.FromResult(result);
        }
        protected string CheckBoxIcon(T item) => MultiSelection ? (_selectedValues.Contains(item) ? Icons.Material.Filled.CheckBox : Icons.Material.Filled.CheckBoxOutlineBlank) : null;
        protected string GetIdForIndex(int index) => $"{_elementId}_{index}";
        protected string CurrentIcon => !string.IsNullOrWhiteSpace(AdornmentIcon) ? AdornmentIcon : _isOpen ? CloseIcon : OpenIcon;
        protected string SelectAllCheckBoxIcon => _selectAllChecked.HasValue ? _selectAllChecked.Value ? CheckedIcon : UncheckedIcon : IndeterminateIcon;
        /// <summary>
        /// Returns whether or not the Value can be found in items. If not, the Select will display it as a string.
        /// </summary>
        protected bool CanRenderValue => Value != null && ChildContent != null;
        protected bool IsValueInList => Items != null && Items.Contains(Value, _comparer);

        /// <summary>
        /// Clear the selection
        /// </summary>
        public async Task Clear()
        {
            await SetValueAsync(default, false);
            await SetTextAsync(default, false);
            _selectedValues.Clear();
            BeginValidate();
            StateHasChanged();
            await SelectedValuesChanged.InvokeAsync(_selectedValues);
        }

        public override ValueTask FocusAsync() => _elementReference.FocusAsync();
        public override ValueTask SelectAsync() => _elementReference.SelectAsync();

        public override ValueTask SelectRangeAsync(int pos1, int pos2) => _elementReference.SelectRangeAsync(pos1, pos2);

        protected override Task UpdateValuePropertyAsync(bool updateText)
        {
            // For MultiSelection of non-string T's we don't update the Value!!!
            if (typeof(T) == typeof(string) || !MultiSelection)
                base.UpdateValuePropertyAsync(updateText);
            return Task.CompletedTask;
        }

        protected override Task UpdateTextPropertyAsync(bool updateValue)
        {
            // when multiselection is true, we return
            // a comma separated list of selected values
            if (MultiSelectionTextFunc != null)
            {
                return MultiSelection
                    ? SetCustomizedTextAsync(string.Join(Delimiter, SelectedValues.Select(x => Converter.Set(x))),
                        selectedConvertedValues: SelectedValues.Select(x => Converter.Set(x)).ToList(),
                        multiSelectionTextFunc: MultiSelectionTextFunc)
                    : base.UpdateTextPropertyAsync(updateValue);
            }
            else
            {
                return MultiSelection
                    ? SetTextAsync(string.Join(Delimiter, SelectedValues.Select(x => Converter.Set(x))))
                    : base.UpdateTextPropertyAsync(updateValue);
            }
        }

        protected override async Task OnAfterRenderAsync(bool firstRender)
        {
            if (firstRender)
            {
                await _keyInterceptor.Connect(_elementId, new KeyInterceptorOptions()
                {
                    //EnableLogging = true,
                    TargetClass = "mud-input-control",
                    Keys = {
                        new KeyOptions { Key=" ", PreventDown = "key+none" }, //prevent scrolling page, toggle open/close
                        new KeyOptions { Key="ArrowUp", PreventDown = "key+none" }, // prevent scrolling page, instead hilight previous item
                        new KeyOptions { Key="ArrowDown", PreventDown = "key+none" }, // prevent scrolling page, instead hilight next item
                        new KeyOptions { Key="Home", PreventDown = "key+none" },
                        new KeyOptions { Key="End", PreventDown = "key+none" },
                        new KeyOptions { Key="Escape" },
                        new KeyOptions { Key="Enter", PreventDown = "key+none" },
                        new KeyOptions { Key="NumpadEnter", PreventDown = "key+none" },
                        new KeyOptions { Key="a", PreventDown = "key+ctrl" }, // select all items instead of all page text
                        new KeyOptions { Key="A", PreventDown = "key+ctrl" }, // select all items instead of all page text
                        new KeyOptions { Key="/./", SubscribeDown = true, SubscribeUp = true }, // for our users
                    },
                });
                _keyInterceptor.KeyDown += HandleKeyDown;
                _keyInterceptor.KeyUp += HandleKeyUp;
            }
            await base.OnAfterRenderAsync(firstRender);
        }

        private Task SelectFirstItem()
        {
            if (Items == null || Items.Any() == false)
                return Task.CompletedTask;
            var lastItem = Items.FirstOrDefault();
            return SelectItemAsync(lastItem, 0);
        }
        private Task SelectFirstItem(string startChar)
        {
            if (Items == null || Items.Any() == false)
                return Task.CompletedTask;
            var lastItem = Items.Select((Item, Index) => (Item, Index)).Where(i => Converter.Set(i.Item).ToLowerInvariant().StartsWith(startChar)).FirstOrDefault();
            return SelectItemAsync(lastItem.Item, lastItem.Index);
        }

        private ValueTask ScrollToItemAsync(int index)
        {
            if (UseVirtualization)
            {
                _cachedCount ??= Items?.Count() ?? 0;
                var relativeTop = index >= 0 && _cachedCount.Value > 0 ? (double)index / (double)_cachedCount.Value : 0.0;
                return ScrollManager.SetRelativeScrollTopAsync(_listId, relativeTop);
            }
            else
            {
                return index >= 0 ? ScrollManager.ScrollToListItemAsync(GetIdForIndex(index)) : ValueTask.CompletedTask;
            }
        }

        ValueTask SelectNextItem(int direction)
        {
            if (direction == 0 || Items == null || Items.Any() == false)
                return ValueTask.CompletedTask;
            _cachedCount ??= Items.Count();
            if (_cachedCount.Value < 1)
                return ValueTask.CompletedTask;
            var index = _activeItemId is int v ? v : 0;
            var newIndex = (10 * _cachedCount.Value + index + direction) % _cachedCount.Value;
            if (index == newIndex)
                return ValueTask.CompletedTask;
            _activeItemId = newIndex;
            return ScrollToItemAsync(newIndex);
        }

        private Task SelectLastItem()
        {
            if (Items == null || Items.Any() == false)
                return Task.CompletedTask;
            var lastItem = Items.LastOrDefault();
            _cachedCount ??= Items.Count();
            return SelectItemAsync(lastItem, _cachedCount.Value - 1);
        }
        private async Task SelectItemAsync(T item, int index)
        {
            if (!MultiSelection)
            {
                _selectedValues.Clear();
                _selectedValues.Add(item);
                await SetValueAsync(item, updateText: true);
            }
            else
            {
                _selectedValues.Add(item);
            }
            _activeItemId = index;
            await _elementReference.SetText(Text);
            await ScrollToItemAsync(index);
        }

        protected override void OnAfterRender(bool firstRender)
        {
            base.OnAfterRender(firstRender);
            lock (this)
            {
                if (_renderComplete != null)
                {
                    _renderComplete.TrySetResult();
                    _renderComplete = null;
                }
            }
        }
        internal async void HandleKeyDown(KeyboardEventArgs obj)
        {
            if (Disabled || ReadOnly)
                return;
            var key = obj.Key.ToLowerInvariant();
            if (_isOpen && key.Length == 1 && key != " " && !(obj.CtrlKey || obj.ShiftKey || obj.AltKey || obj.MetaKey))
            {
                await SelectFirstItem(key);
                return;
            }
            switch (obj.Key)
            {
                case "Tab":
                    await CloseMenu(false);
                    break;
                case "ArrowUp":
                    if (obj.AltKey == true)
                    {
                        await CloseMenu();
                        break;
                    }
                    else if (_isOpen == false)
                    {
                        await OpenMenu();
                        break;
                    }
                    else
                    {
                        await SelectNextItem(-1);
                        break;
                    }
                case "ArrowDown":
                    if (obj.AltKey == true)
                    {
                        await OpenMenu();
                        break;
                    }
                    else if (_isOpen == false)
                    {
                        await OpenMenu();
                        break;
                    }
                    else
                    {
                        await SelectNextItem(+1);
                        break;
                    }
                case " ":
                    await ToggleMenu();
                    break;
                case "Escape":
                    await CloseMenu(true);
                    break;
                case "Home":
                    await SelectFirstItem();
                    break;
                case "End":
                    await SelectLastItem();
                    break;
                case "Enter":
                case "NumpadEnter":
                    var (item, index) = GetActiveItemAndIndex();
                    if (!MultiSelection)
                    {
                        if (!_isOpen)
                        {
                            await OpenMenu();
                            return;
                        }
                        // this also closes the menu
                        await SelectOption(item, index);
                        break;
                    }
                    else
                    {
                        if (_isOpen == false)
                        {
                            await OpenMenu();
                            break;
                        }
                        else
                        {
                            await SelectOption(item, index);
                            await _elementReference.SetText(Text);
                            break;
                        }
                    }
                case "a":
                case "A":
                    if (obj.CtrlKey == true)
                    {
                        if (MultiSelection)
                        {
                            await SelectAllClickAsync();
                            //If we didn't add delay, it won't work.
                            await WaitForRender();
                            await Task.Delay(1);
                            StateHasChanged();
                            //It only works when selecting all, not render unselect all.
                            //UpdateSelectAllChecked();
                        }
                    }
                    break;
            }
            OnKeyDown.InvokeAsync(obj).AndForget();

        }

        private Task WaitForRender()
        {
            Task t = null;
            lock (this)
            {
                if (_renderComplete != null)
                    return _renderComplete.Task;
                _renderComplete = new TaskCompletionSource();
                t = _renderComplete.Task;
            }
            StateHasChanged();
            return t;
        }

        private TaskCompletionSource _renderComplete;

        internal void HandleKeyUp(KeyboardEventArgs obj)
        {
            OnKeyUp.InvokeAsync(obj).AndForget();
        }

        private async Task SelectAllClickAsync()
        {
            // Manage the fake tri-state of a checkbox
            if (!_selectAllChecked.HasValue)
                _selectAllChecked = true;
            else if (_selectAllChecked.Value)
                _selectAllChecked = false;
            else
                _selectAllChecked = true;
            // Define the items selection
            if (_selectAllChecked.Value == true)
                await SelectAllItems();
            else
                await Clear();
        }

        private void UpdateSelectAllChecked()
        {
            if (MultiSelection && SelectAll)
            {
                _cachedCount ??= Items?.Count() ?? 0;
                if (_selectedValues.Count == 0)
                {
                    _selectAllChecked = false;
                }
                else if (_cachedCount.Value == _selectedValues.Count)
                {
                    _selectAllChecked = true;
                }
                else
                {
                    _selectAllChecked = null;
                }
            }
        }

        protected async Task SetCustomizedTextAsync(string text, bool updateValue = true,
            List<string> selectedConvertedValues = null,
            Func<List<string>, string> multiSelectionTextFunc = null)
        {
            // The Text property of the control is updated
            Text = multiSelectionTextFunc?.Invoke(selectedConvertedValues);

            // The comparison is made on the multiSelectionText variable
            if (_multiSelectionText != text)
            {
                _multiSelectionText = text;
                if (!string.IsNullOrWhiteSpace(_multiSelectionText))
                    Touched = true;
                if (updateValue)
                    await UpdateValuePropertyAsync(false);
                await TextChanged.InvokeAsync(_multiSelectionText);
            }
        }

        private async Task SelectAllItems()
        {
            if (!MultiSelection)
                return;
            var selectedValues = Items != null ? new HashSet<T>(Items, _comparer) : new HashSet<T>(_comparer);
            _selectedValues = selectedValues;
            if (MultiSelectionTextFunc != null)
            {
                await SetCustomizedTextAsync(string.Join(Delimiter, SelectedValues.Select(x => Converter.Set(x))),
                    selectedConvertedValues: SelectedValues.Select(x => Converter.Set(x)).ToList(),
                    multiSelectionTextFunc: MultiSelectionTextFunc);
            }
            else
            {
                await SetTextAsync(string.Join(Delimiter, SelectedValues.Select(x => Converter.Set(x))), updateValue: false);
            }
            UpdateSelectAllChecked();
            _selectedValues = selectedValues; // need to force selected values because Blazor overwrites it under certain circumstances due to changes of Text or Value
            BeginValidate();
            await SelectedValuesChanged.InvokeAsync(SelectedValues);
            if (MultiSelection && typeof(T) == typeof(string))
                SetValueAsync((T)(object)Text, updateText: false).AndForget();
        }

        /// <summary>
        /// Extra handler for clearing selection.
        /// </summary>
        protected async ValueTask SelectClearButtonClickHandlerAsync(MouseEventArgs e)
        {
            await SetValueAsync(default, false);
            await SetTextAsync(default, false);
            _selectedValues.Clear();
            BeginValidate();
            StateHasChanged();
            await SelectedValuesChanged.InvokeAsync(_selectedValues);
            await OnClearButtonClick.InvokeAsync(e);
        }

        internal void OnLostFocus(FocusEventArgs obj)
        {
            if (_isOpen)
            {
                // when the menu is open we immediately get back the focus if we lose it (i.e. because of checkboxes in multi-select)
                // otherwise we can't receive key strokes any longer
                _elementReference.FocusAsync().AndForget(TaskOption.Safe);
            }
            base.OnBlur.InvokeAsync(obj);
        }

        public async Task ToggleMenu()
        {
            if (Disabled || ReadOnly)
                return;
            if (_isOpen)
                await CloseMenu(true);
            else
                await OpenMenu();
        }

        protected T GetItemFromIndex(int index)
        {
            if (Items == null)
                return default;
            if (index >= 0 && Items is IReadOnlyList<T> list && index < list.Count)
                return list[index];
            return Items.Skip(index).FirstOrDefault();
        }
        /// <summary>
        /// Returns the active item value and index
        /// </summary>
        protected (T Item, int Index) GetActiveItemAndIndex()
        {
            if (Items != null && _activeItemId is int index && index >= 0)
            {
                return (GetItemFromIndex(index), index);
            }
            else
            {
                return (default, -1);
            }
        }
        public async Task OpenMenu()
        {
            if (Disabled || ReadOnly)
                return;
            _isOpen = true;
            StateHasChanged();
            // we need the popover to be visibible
            await WaitForRender();
            // Scroll the active item on each opening
            var (item, index) = GetActiveItemAndIndex();
            await ScrollToItemAsync(index);
            //disable escape propagation: if selectmenu is open, only the select popover should close and underlying components should not handle escape key
            await _keyInterceptor.UpdateKey(new() { Key = "Escape", StopDown = "Key+none" });
            await OnOpen.InvokeAsync();
        }

        public async Task CloseMenu(bool focusAgain = true)
        {
            _isOpen = false;
            if (focusAgain == true)
            {
                StateHasChanged();
                await OnBlur.InvokeAsync(new FocusEventArgs());
                _elementReference.FocusAsync().AndForget(TaskOption.Safe);
                StateHasChanged();
            }

            //enable escape propagation: the select popover was closed, now underlying components are allowed to handle escape key
            await _keyInterceptor.UpdateKey(new() { Key = "Escape", StopDown = "none" });
            await OnClose.InvokeAsync();
        }

        /// <summary>
        /// Defines how values are displayed in the drop-down list
        /// </summary>
        [Parameter]
        [Category(CategoryTypes.FormComponent.ListBehavior)]
        public Func<T, string> ToStringFunc
        {
            get => _toStringFunc;
            set
            {
                if (_toStringFunc == value)
                    return;
                _toStringFunc = value;
                Converter = new Converter<T>
                {
                    SetFunc = _toStringFunc ?? (x => x?.ToString()),
                    //GetFunc = LookupValue,
                };
            }
        }

        /// <summary>
        /// Render template for am item
        /// </summary>
        [Parameter]
        [Category(CategoryTypes.FormComponent.Data)]
        public RenderFragment<T> ChildContent { get; set; }

        /// <summary>
        /// User class names for the popover, separated by space
        /// </summary>
        [Parameter]
        [Category(CategoryTypes.FormComponent.ListAppearance)]
        public string PopoverClass { get; set; }

        /// <summary>
        /// If true, compact vertical padding will be applied to all Select items.
        /// </summary>
        [Parameter]
        [Category(CategoryTypes.FormComponent.ListAppearance)]
        public bool Dense { get; set; }

        /// <summary>
        /// The Close Select Icon
        /// </summary>
        [Parameter]
        [Category(CategoryTypes.FormComponent.Appearance)]
        public string CloseIcon { get; set; } = Icons.Material.Filled.ArrowDropUp;

        /// <summary>
        /// If set to true and the MultiSelection option is set to true, a "select all" checkbox is added at the top of the list of items.
        /// </summary>
        [Parameter]
        [Category(CategoryTypes.FormComponent.ListBehavior)]
        public bool SelectAll { get; set; }

        /// <summary>
        /// Define the text of the Select All option.
        /// </summary>
        [Parameter]
        [Category(CategoryTypes.FormComponent.ListAppearance)]
        public string SelectAllText { get; set; } = "Select all";

        /// <summary>
        /// Custom checked icon.
        /// </summary>
        [Parameter]
        [Category(CategoryTypes.FormComponent.ListAppearance)]
        public string CheckedIcon { get; set; } = Icons.Material.Filled.CheckBox;

        /// <summary>
        /// Custom unchecked icon.
        /// </summary>
        [Parameter]
        [Category(CategoryTypes.FormComponent.ListAppearance)]
        public string UncheckedIcon { get; set; } = Icons.Material.Filled.CheckBoxOutlineBlank;

        /// <summary>
        /// Custom indeterminate icon.
        /// </summary>
        [Parameter]
        [Category(CategoryTypes.FormComponent.ListAppearance)]
        public string IndeterminateIcon { get; set; } = Icons.Material.Filled.IndeterminateCheckBox;

        /// <summary>
        /// The Open Select Icon
        /// </summary>
        [Parameter]
        [Category(CategoryTypes.FormComponent.Appearance)]
        public string OpenIcon { get; set; } = Icons.Material.Filled.ArrowDropDown;

        /// <summary>
        /// Set of selected values. If MultiSelection is false it will only ever contain a single value. This property is two-way bindable.
        /// </summary>
        [Parameter]
        [Category(CategoryTypes.FormComponent.Data)]
        public IEnumerable<T> SelectedValues
        {
            get
            {
                if (_selectedValues == null)
                    _selectedValues = new HashSet<T>(_comparer);
                return _selectedValues;
            }
            set
            {
                var set = value ?? new HashSet<T>(_comparer);
                if (SelectedValues.Count() == set.Count() && _selectedValues.All(x => set.Contains(x)))
                    return;
                _selectedValues = new HashSet<T>(set, _comparer);
                if (!MultiSelection)
                    SetValueAsync(_selectedValues.FirstOrDefault()).AndForget();
                else
                {
                    //Warning. Here the Converter was not set yet
                    if (MultiSelectionTextFunc != null)
                    {
                        SetCustomizedTextAsync(string.Join(Delimiter, SelectedValues.Select(x => Converter.Set(x))),
                            selectedConvertedValues: SelectedValues.Select(x => Converter.Set(x)).ToList(),
                            multiSelectionTextFunc: MultiSelectionTextFunc).AndForget();
                    }
                    else
                    {
                        SetTextAsync(string.Join(Delimiter, SelectedValues.Select(x => Converter.Set(x))), updateValue: false).AndForget();
                    }
                }
                SelectedValuesChanged.InvokeAsync(new HashSet<T>(SelectedValues, _comparer));
                if (MultiSelection && typeof(T) == typeof(string))
                    SetValueAsync((T)(object)Text, updateText: false).AndForget();
            }
        }

        async Task SelectOption(T value, int index)
        {
            if (MultiSelection)
            {
                // multi-selection: menu stays open
                if (!_selectedValues.Contains(value))
                    _selectedValues.Add(value);
                else
                    _selectedValues.Remove(value);

                if (MultiSelectionTextFunc != null)
                {
                    await SetCustomizedTextAsync(string.Join(Delimiter, SelectedValues.Select(x => Converter.Set(x))),
                        selectedConvertedValues: SelectedValues.Select(x => Converter.Set(x)).ToList(),
                        multiSelectionTextFunc: MultiSelectionTextFunc);
                }
                else
                {
                    await SetTextAsync(string.Join(Delimiter, SelectedValues.Select(x => Converter.Set(x))), updateValue: false);
                }

                UpdateSelectAllChecked();
                BeginValidate();
            }
            else
            {
                // single selection
                _isOpen = false;

                if (EqualityComparer<T>.Default.Equals(Value, value))
                {
                    StateHasChanged();
                    return;
                }

                await SetValueAsync(value);
                _elementReference.SetText(Text).AndForget();
                _selectedValues.Clear();
                _selectedValues.Add(value);
            }

            await SelectedValuesChanged.InvokeAsync(SelectedValues);
            if (MultiSelection && typeof(T) == typeof(string))
                await SetValueAsync((T)(object)Text, updateText: false);
            StateHasChanged();
        }

        /// <summary>
        /// The Comparer to use for comparing selected values internally.
        /// </summary>
        [Parameter]
        [Category(CategoryTypes.FormComponent.Behavior)]
        public IEqualityComparer<T> Comparer
        {
            get => _comparer;
            set
            {
                _comparer = value;
                // Apply comparer and refresh selected values
                _selectedValues = new HashSet<T>(_selectedValues, _comparer);
                SelectedValues = _selectedValues;
            }
        }

        /// <summary>
        /// Sets the maxheight the Select can have when open.
        /// </summary>
        [Parameter]
        [Category(CategoryTypes.FormComponent.ListAppearance)]
        public int MaxHeight { get; set; } = 300;

        /// <summary>
        /// Set the anchor origin point to determen where the popover will open from.
        /// </summary>
        [Parameter]
        [Category(CategoryTypes.FormComponent.ListAppearance)]
        public Origin AnchorOrigin { get; set; } = Origin.TopCenter;

        /// <summary>
        /// Sets the transform origin point for the popover.
        /// </summary>
        [Parameter]
        [Category(CategoryTypes.FormComponent.ListAppearance)]
        public Origin TransformOrigin { get; set; } = Origin.TopCenter;

        /// <summary>
        /// Fires when SelectedValues changes.
        /// </summary>
        [Parameter] public EventCallback<IEnumerable<T>> SelectedValuesChanged { get; set; }

        /// <summary>
        /// Function to define a customized multiselection text.
        /// </summary>
        [Parameter]
        [Category(CategoryTypes.FormComponent.Behavior)]
        public Func<List<string>, string> MultiSelectionTextFunc { get; set; }

        /// <summary>
        /// Parameter to define the delimited string separator.
        /// </summary>
        [Parameter]
        [Category(CategoryTypes.FormComponent.Behavior)]
        public string Delimiter { get; set; } = ", ";

        /// <summary>
        /// If true, multiple values can be selected via checkboxes which are automatically shown in the dropdown
        /// </summary>
        [Parameter]
        [Category(CategoryTypes.FormComponent.ListBehavior)]
        public bool MultiSelection { get; set; }

        /// <summary>
        /// If true, multiple values can be selected via checkboxes which are automatically shown in the dropdown
        /// </summary>
        [Parameter]
        [Category(CategoryTypes.FormComponent.ListBehavior)]
        public bool UseVirtualization { get; set; }

        [Parameter]
        [Category(CategoryTypes.FormComponent.ListBehavior)]
        public IEnumerable<T> Items { get; set; }

        /// <summary>
        /// If true, the Select's input will not show any values that are not defined in the dropdown.
        /// This can be useful if Value is bound to a variable which is initialized to a value which is not in the list
        /// and you want the Select to show the label / placeholder instead.
        /// </summary>
        [Parameter]
        [Category(CategoryTypes.FormComponent.Behavior)]
        public bool Strict { get; set; }

        /// <summary>
        /// Show clear button.
        /// </summary>
        [Parameter]
        [Category(CategoryTypes.FormComponent.Behavior)]
        public bool Clearable { get; set; } = false;

        /// <summary>
        /// If true, prevent scrolling while dropdown is open.
        /// </summary>
        [Parameter]
        [Category(CategoryTypes.FormComponent.ListBehavior)]
        public bool LockScroll { get; set; } = false;

        /// <summary>
        /// Button click event for clear button. Called after text and value has been cleared.
        /// </summary>
        [Parameter] public EventCallback<MouseEventArgs> OnClearButtonClick { get; set; }

        /// <summary>
        /// Fired when dropdown opens.
        /// </summary>
        [Category(CategoryTypes.FormComponent.Behavior)]
        [Parameter] public EventCallback OnOpen { get; set; }

        /// <summary>
        /// Fired when dropdown closes.
        /// </summary>
        [Category(CategoryTypes.FormComponent.Behavior)]
        [Parameter] public EventCallback OnClose { get; set; }
    }
}
