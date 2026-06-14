// Extension: squad-messaging
// Provides tools for Copilot sessions to send and receive messages on the squad messaging bus

import { joinSession } from "@github/copilot-sdk/extension";

const API_BASE = process.env.MESSAGING_API_URL || "http://localhost:5000";

const session = await joinSession({
    tools: [
        {
            name: "squad_send_message",
            description:
                "Send a message on the squad messaging bus. Use this to communicate with other squads or reply to the user.",
            parameters: {
                type: "object",
                properties: {
                    from: {
                        type: "string",
                        description: "Your identity (e.g. 'qa-squad', 'game-development-squad', 'site-design-squad', 'research-and-ideation-squad')",
                    },
                    to: {
                        type: "string",
                        description: "Recipient squad name, 'coordinator', or 'user'",
                    },
                    subject: {
                        type: "string",
                        description: "Brief subject line",
                    },
                    body: {
                        type: "string",
                        description: "The message content",
                    },
                },
                required: ["from", "to", "body"],
            },
            skipPermission: true,
            handler: async (args) => {
                try {
                    const res = await fetch(`${API_BASE}/api/messages`, {
                        method: "POST",
                        headers: { "Content-Type": "application/json" },
                        body: JSON.stringify({
                            from: args.from,
                            to: args.to,
                            subject: args.subject || args.body.slice(0, 50),
                            body: args.body,
                        }),
                    });
                    if (!res.ok) {
                        const text = await res.text();
                        return { textResultForLlm: `Failed to send: ${res.status} ${text}`, resultType: "failure" };
                    }
                    const msg = await res.json();
                    return `Message sent successfully (id: ${msg.id})`;
                } catch (err) {
                    return { textResultForLlm: `Error sending message: ${err.message}`, resultType: "failure" };
                }
            },
        },
        {
            name: "squad_read_recent_messages",
            description:
                "Read recent messages from the squad messaging bus. Use this to see what's been discussed and catch up on context.",
            parameters: {
                type: "object",
                properties: {
                    limit: {
                        type: "number",
                        description: "Number of recent messages to fetch (default: 20, max: 100)",
                    },
                },
            },
            skipPermission: true,
            handler: async (args) => {
                try {
                    const limit = args.limit || 20;
                    const res = await fetch(`${API_BASE}/api/messages/recent?limit=${limit}`);
                    if (!res.ok) {
                        return { textResultForLlm: `Failed to fetch: ${res.status}`, resultType: "failure" };
                    }
                    const messages = await res.json();
                    if (messages.length === 0) return "No recent messages.";

                    const formatted = messages.map((m) =>
                        `[${m.from} → ${m.to}] ${m.body}`
                    ).join("\n");
                    return `Recent messages (${messages.length}):\n${formatted}`;
                } catch (err) {
                    return { textResultForLlm: `Error reading messages: ${err.message}`, resultType: "failure" };
                }
            },
        },
        {
            name: "squad_read_inbox",
            description:
                "Read unread messages in a specific squad's inbox. Use this to check what messages have been sent to your squad.",
            parameters: {
                type: "object",
                properties: {
                    squad: {
                        type: "string",
                        description: "The squad name to check inbox for (e.g. 'qa-squad')",
                    },
                    unreadOnly: {
                        type: "boolean",
                        description: "If true, only return unread messages (default: true)",
                    },
                },
                required: ["squad"],
            },
            skipPermission: true,
            handler: async (args) => {
                try {
                    const unread = args.unreadOnly !== false ? "true" : "false";
                    const res = await fetch(
                        `${API_BASE}/api/messages/${args.squad}/inbox?unreadOnly=${unread}`
                    );
                    if (!res.ok) {
                        return { textResultForLlm: `Failed to fetch inbox: ${res.status}`, resultType: "failure" };
                    }
                    const messages = await res.json();
                    if (messages.length === 0) return `No messages in ${args.squad} inbox.`;

                    const formatted = messages.map((m) =>
                        `[${m.from}] ${m.body}`
                    ).join("\n");
                    return `${args.squad} inbox (${messages.length} messages):\n${formatted}`;
                } catch (err) {
                    return { textResultForLlm: `Error reading inbox: ${err.message}`, resultType: "failure" };
                }
            },
        },
    ],
});

await session.log("Squad messaging extension loaded — bus tools available");
