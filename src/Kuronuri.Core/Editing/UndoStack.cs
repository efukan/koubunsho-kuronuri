using System;
using System.Collections.Generic;

namespace Kuronuri.Core.Editing
{
    /// <summary>
    /// Undo / redo history over an <see cref="ItemList"/>. At most <see cref="MaxHistory"/> commands are kept (the oldest
    /// are dropped). "Modified" is decided by comparing the current history position with the one recorded at save time.
    /// The history is never written to a file.
    /// </summary>
    public sealed class UndoStack
    {
        public const int MaxHistory = 500;

        private readonly ItemList _list;
        private readonly LinkedList<(EditCommand Cmd, long Serial)> _undo = new LinkedList<(EditCommand, long)>();
        private readonly Stack<(EditCommand Cmd, long Serial)> _redo = new Stack<(EditCommand, long)>();
        private long _nextSerial = 1;
        private long _baseSerial;    // state id when the undo list is empty (serial of the last dropped command)
        private long _savedSerial;

        public UndoStack(ItemList list) { _list = list; }

        public event EventHandler? StateChanged;

        public bool CanUndo => _undo.Count > 0;
        public bool CanRedo => _redo.Count > 0;
        public int UndoCount => _undo.Count;
        public int RedoCount => _redo.Count;

        /// <summary>Identifies the current document state; equal ids mean equal content.</summary>
        public long CurrentStateId => _undo.Count == 0 ? _baseSerial : _undo.Last!.Value.Serial;

        public bool IsModified => CurrentStateId != _savedSerial;

        public void MarkSaved() { _savedSerial = CurrentStateId; StateChanged?.Invoke(this, EventArgs.Empty); }

        /// <summary>Forgets the history (e.g. when another document is opened). The current state counts as saved.</summary>
        public void Clear()
        {
            _undo.Clear(); _redo.Clear();
            _baseSerial = _nextSerial++;
            _savedSerial = _baseSerial;
            StateChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>Clears the history and marks the state as unsaved (e.g. a fresh original that has never been saved with redactions).</summary>
        public void ClearAsModified()
        {
            Clear();
            _savedSerial = -1;
            StateChanged?.Invoke(this, EventArgs.Empty);
        }

        public void Execute(EditCommand cmd)
        {
            cmd.Apply(_list);
            _undo.AddLast((cmd, _nextSerial++));
            _redo.Clear();
            while (_undo.Count > MaxHistory)
            {
                _baseSerial = _undo.First!.Value.Serial;
                _undo.RemoveFirst();
            }
            _list.RaiseChanged();
            StateChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <returns>The command that was undone (callers select its <see cref="EditCommand.ItemsBefore"/>), or null.</returns>
        public EditCommand? Undo()
        {
            if (_undo.Count == 0) return null;
            var top = _undo.Last!.Value;
            top.Cmd.Revert(_list);
            _undo.RemoveLast();
            _redo.Push(top);
            _list.RaiseChanged();
            StateChanged?.Invoke(this, EventArgs.Empty);
            return top.Cmd;
        }

        /// <returns>The command that was redone (callers select its <see cref="EditCommand.ItemsAfter"/>), or null.</returns>
        public EditCommand? Redo()
        {
            if (_redo.Count == 0) return null;
            var top = _redo.Pop();
            top.Cmd.Apply(_list);
            _undo.AddLast(top);
            _list.RaiseChanged();
            StateChanged?.Invoke(this, EventArgs.Empty);
            return top.Cmd;
        }
    }
}
