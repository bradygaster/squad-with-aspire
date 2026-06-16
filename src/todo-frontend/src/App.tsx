import { useState, useEffect, useCallback } from 'react';
import type { Todo, FilterMode } from './types';
import { fetchTodos, createTodo, updateTodo, deleteTodo } from './api';
import { TodoInput } from './components/TodoInput';
import { TodoItem } from './components/TodoItem';
import { FilterTabs } from './components/FilterTabs';

export default function App() {
  const [todos, setTodos] = useState<Todo[]>([]);
  const [filter, setFilter] = useState<FilterMode>('all');
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const loadTodos = useCallback(async () => {
    try {
      const data = await fetchTodos();
      setTodos(data);
      setError(null);
    } catch {
      setError('Failed to load todos. Is the API running?');
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => { loadTodos(); }, [loadTodos]);

  async function handleAdd(title: string) {
    try {
      const newTodo = await createTodo(title);
      setTodos((prev) => [...prev, newTodo]);
    } catch {
      setError('Failed to add todo');
    }
  }

  async function handleToggle(id: string, isComplete: boolean) {
    try {
      const updated = await updateTodo(id, { isComplete });
      setTodos((prev) => prev.map((t) => (t.id === id ? updated : t)));
    } catch {
      setError('Failed to update todo');
    }
  }

  async function handleDelete(id: string) {
    try {
      await deleteTodo(id);
      setTodos((prev) => prev.filter((t) => t.id !== id));
    } catch {
      setError('Failed to delete todo');
    }
  }

  async function handleEdit(id: string, title: string) {
    try {
      const updated = await updateTodo(id, { title });
      setTodos((prev) => prev.map((t) => (t.id === id ? updated : t)));
    } catch {
      setError('Failed to update todo');
    }
  }

  function handleClearCompleted() {
    const completed = todos.filter((t) => t.isComplete);
    completed.forEach((t) => handleDelete(t.id));
  }

  const filteredTodos = todos.filter((t) => {
    if (filter === 'active') return !t.isComplete;
    if (filter === 'completed') return t.isComplete;
    return true;
  });

  const activeCount = todos.filter((t) => !t.isComplete).length;
  const hasCompleted = todos.some((t) => t.isComplete);

  if (loading) {
    return <div className="app"><div className="card"><p className="loading">Loading...</p></div></div>;
  }

  return (
    <div className="app">
      <div className="card">
        <h1>Todo List</h1>
        {error && <p className="error" role="alert">{error}</p>}
        <TodoInput onAdd={handleAdd} />
        {todos.length === 0 ? (
          <p className="empty-state">📝 No todos yet. Add one above!</p>
        ) : (
          <>
            <ul className="todo-list" aria-label="Todo items">
              {filteredTodos.map((todo) => (
                <TodoItem
                  key={todo.id}
                  todo={todo}
                  onToggle={handleToggle}
                  onDelete={handleDelete}
                  onEdit={handleEdit}
                />
              ))}
            </ul>
            <FilterTabs current={filter} onChange={setFilter} activeCount={activeCount} />
            {hasCompleted && (
              <button className="clear-completed" onClick={handleClearCompleted}>
                Clear completed
              </button>
            )}
          </>
        )}
      </div>
    </div>
  );
}
