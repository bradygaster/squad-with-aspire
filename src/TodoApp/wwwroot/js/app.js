(function () {
    'use strict';

    const API_URL = '/api/todos';
    let todos = [];
    let currentFilter = 'all';

    // DOM elements
    const input = document.getElementById('todo-input');
    const addBtn = document.getElementById('add-btn');
    const todoList = document.getElementById('todo-list');
    const emptyState = document.getElementById('empty-state');
    const footer = document.getElementById('footer');
    const itemCount = document.getElementById('item-count');
    const filterBtns = document.querySelectorAll('.filter-btn');

    // Initialize
    fetchTodos();

    // Event listeners
    addBtn.addEventListener('click', addTodo);
    input.addEventListener('keydown', (e) => {
        if (e.key === 'Enter') addTodo();
    });
    filterBtns.forEach(btn => {
        btn.addEventListener('click', () => {
            currentFilter = btn.dataset.filter;
            filterBtns.forEach(b => {
                b.classList.remove('active');
                b.setAttribute('aria-selected', 'false');
            });
            btn.classList.add('active');
            btn.setAttribute('aria-selected', 'true');
            render();
        });
    });

    async function fetchTodos() {
        try {
            const res = await fetch(API_URL);
            todos = await res.json();
            render();
        } catch (err) {
            console.error('Failed to fetch todos:', err);
        }
    }

    async function addTodo() {
        const title = input.value.trim();
        if (!title) return;

        try {
            const res = await fetch(API_URL, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ title })
            });
            if (res.ok) {
                const todo = await res.json();
                todos.push(todo);
                input.value = '';
                render();
                input.focus();
            }
        } catch (err) {
            console.error('Failed to add todo:', err);
        }
    }

    async function toggleTodo(id) {
        const todo = todos.find(t => t.id === id);
        if (!todo) return;

        try {
            const res = await fetch(`${API_URL}/${id}`, {
                method: 'PUT',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ isCompleted: !todo.isCompleted })
            });
            if (res.ok) {
                const updated = await res.json();
                const idx = todos.findIndex(t => t.id === id);
                todos[idx] = updated;
                render();
            }
        } catch (err) {
            console.error('Failed to toggle todo:', err);
        }
    }

    async function deleteTodo(id) {
        try {
            const res = await fetch(`${API_URL}/${id}`, { method: 'DELETE' });
            if (res.ok || res.status === 204) {
                todos = todos.filter(t => t.id !== id);
                render();
            }
        } catch (err) {
            console.error('Failed to delete todo:', err);
        }
    }

    function getFilteredTodos() {
        switch (currentFilter) {
            case 'active': return todos.filter(t => !t.isCompleted);
            case 'completed': return todos.filter(t => t.isCompleted);
            default: return todos;
        }
    }

    function render() {
        const filtered = getFilteredTodos();
        const activeCount = todos.filter(t => !t.isCompleted).length;

        // Empty state
        if (todos.length === 0) {
            emptyState.hidden = false;
            footer.hidden = true;
            todoList.innerHTML = '';
            return;
        }

        emptyState.hidden = true;
        footer.hidden = false;
        itemCount.textContent = `${activeCount} item${activeCount !== 1 ? 's' : ''} left`;

        todoList.innerHTML = filtered.map(todo => `
            <li class="todo-item ${todo.isCompleted ? 'completed' : ''}" data-id="${todo.id}">
                <button class="todo-checkbox" 
                        role="checkbox" 
                        aria-checked="${todo.isCompleted}" 
                        aria-label="${todo.isCompleted ? 'Mark as incomplete' : 'Mark as complete'}"
                        data-action="toggle"></button>
                <span class="todo-text">${escapeHtml(todo.title)}</span>
                <button class="delete-btn" 
                        aria-label="Delete todo" 
                        data-action="delete">×</button>
            </li>
        `).join('');

        // Attach event listeners
        todoList.querySelectorAll('[data-action="toggle"]').forEach(btn => {
            btn.addEventListener('click', () => {
                const id = btn.closest('.todo-item').dataset.id;
                toggleTodo(id);
            });
        });
        todoList.querySelectorAll('[data-action="delete"]').forEach(btn => {
            btn.addEventListener('click', () => {
                const id = btn.closest('.todo-item').dataset.id;
                deleteTodo(id);
            });
        });
    }

    function escapeHtml(text) {
        const div = document.createElement('div');
        div.textContent = text;
        return div.innerHTML;
    }
})();
