import { useState } from 'react';
import type { FormEvent } from 'react';

interface TodoInputProps {
  onAdd: (title: string) => void;
}

export function TodoInput({ onAdd }: TodoInputProps) {
  const [text, setText] = useState('');

  function handleSubmit(e: FormEvent) {
    e.preventDefault();
    const trimmed = text.trim();
    if (!trimmed) return;
    onAdd(trimmed);
    setText('');
  }

  return (
    <form className="todo-input" onSubmit={handleSubmit}>
      <input
        type="text"
        value={text}
        onChange={(e) => setText(e.target.value)}
        placeholder="What needs to be done?"
        aria-label="New todo text"
        autoFocus
      />
      <button type="submit" disabled={!text.trim()}>
        Add
      </button>
    </form>
  );
}
