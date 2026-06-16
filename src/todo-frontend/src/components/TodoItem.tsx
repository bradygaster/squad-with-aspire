import { useState } from 'react';
import type { Todo } from '../types';

interface TodoItemProps {
  todo: Todo;
  onToggle: (id: string, isComplete: boolean) => void;
  onDelete: (id: string) => void;
  onEdit: (id: string, title: string) => void;
}

export function TodoItem({ todo, onToggle, onDelete, onEdit }: TodoItemProps) {
  const [editing, setEditing] = useState(false);
  const [editText, setEditText] = useState(todo.title);

  function handleDoubleClick() {
    setEditing(true);
    setEditText(todo.title);
  }

  function handleEditSubmit() {
    const trimmed = editText.trim();
    if (trimmed && trimmed !== todo.title) {
      onEdit(todo.id, trimmed);
    }
    setEditing(false);
  }

  function handleEditKeyDown(e: React.KeyboardEvent) {
    if (e.key === 'Enter') handleEditSubmit();
    if (e.key === 'Escape') setEditing(false);
  }

  return (
    <li className={`todo-item ${todo.isComplete ? 'completed' : ''}`}>
      <input
        type="checkbox"
        checked={todo.isComplete}
        onChange={() => onToggle(todo.id, !todo.isComplete)}
        aria-checked={todo.isComplete}
        aria-label={`Mark "${todo.title}" as ${todo.isComplete ? 'incomplete' : 'complete'}`}
      />
      {editing ? (
        <input
          className="edit-input"
          type="text"
          value={editText}
          onChange={(e) => setEditText(e.target.value)}
          onBlur={handleEditSubmit}
          onKeyDown={handleEditKeyDown}
          autoFocus
          aria-label="Edit todo text"
        />
      ) : (
        <span className="todo-text" onDoubleClick={handleDoubleClick}>
          {todo.title}
        </span>
      )}
      {!editing && (
        <button
          className="edit-btn"
          onClick={handleDoubleClick}
          aria-label={`Edit todo: ${todo.title}`}
          data-testid={`edit-todo-${todo.id}`}
        >
          ✏️
        </button>
      )}
      <button
        className="delete-btn"
        onClick={() => onDelete(todo.id)}
        aria-label={`Delete "${todo.title}"`}
      >
        &times;
      </button>
    </li>
  );
}
