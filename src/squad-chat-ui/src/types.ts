export interface SquadMessage {
  id: string;
  from: string;
  to: string;
  subject: string;
  body: string;
  correlationId?: string;
  replyTo?: string;
  timestamp: string;
  isRead: boolean;
}

export interface Squad {
  name: string;
  color: string;
  isActive: boolean;
  unreadCount: number;
}
