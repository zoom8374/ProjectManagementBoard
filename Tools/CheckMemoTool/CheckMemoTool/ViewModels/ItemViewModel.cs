using System;
using CheckMemoTool.Models;
using CheckMemoTool.Services;

namespace CheckMemoTool.ViewModels
{
    public class ItemViewModel : ObservableObject
    {
        private string _text;
        private string _priority;
        private bool _isDone;
        private bool _isEditing;
        private string _editText;

        public string Id { get; private set; }
        public string CreatedAt { get; private set; }
        public string DoneAt { get; set; }

        // 같은 중요도 그룹 안에서의 순서
        public int Order { get; set; }

        public string Text
        {
            get { return _text; }
            set { SetField(ref _text, value); }
        }

        // "high" / "medium" / "low" / null
        public string Priority
        {
            get { return _priority; }
            set
            {
                if (SetField(ref _priority, Models.Priority.Normalize(value)))
                {
                    OnPropertyChanged("PriorityRank");
                }
            }
        }

        public int PriorityRank
        {
            get { return Models.Priority.Rank(_priority); }
        }

        public bool IsDone
        {
            get { return _isDone; }
            set { SetField(ref _isDone, value); }
        }

        public bool IsEditing
        {
            get { return _isEditing; }
            set { SetField(ref _isEditing, value); }
        }

        public string EditText
        {
            get { return _editText; }
            set { SetField(ref _editText, value); }
        }

        public DateTimeOffset DoneTime
        {
            get { return TimeText.Parse(DoneAt); }
        }

        public static ItemViewModel Create(string text)
        {
            return new ItemViewModel
            {
                Id = Guid.NewGuid().ToString(),
                CreatedAt = TimeText.Now(),
                _text = text
            };
        }

        public static ItemViewModel FromData(ItemData d)
        {
            return new ItemViewModel
            {
                Id = d.Id,
                CreatedAt = d.CreatedAt,
                DoneAt = d.DoneAt,
                Order = d.Order,
                _text = d.Text,
                _priority = Models.Priority.Normalize(d.Priority),
                _isDone = d.IsDone
            };
        }

        public ItemData ToData()
        {
            return new ItemData
            {
                Id = Id,
                Text = _text,
                Priority = _priority,
                IsDone = _isDone,
                Order = Order,
                CreatedAt = CreatedAt,
                DoneAt = _isDone ? DoneAt : null
            };
        }
    }
}
