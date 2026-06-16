export interface Todo {
  id: string;
  title: string;
  isComplete: boolean;
  createdAt: string;
}

export type FilterMode = 'all' | 'active' | 'completed';
