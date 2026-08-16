package chatbdi;

import artifact.lib.model.WsMessage;
import artifact.lib.utils.ObjectMapperUtils;
import artifact.lib.websocket.WsClient;
import com.fasterxml.jackson.core.type.TypeReference;
import jason.asSyntax.parser.ParseException;

import java.net.URI;
import java.util.ArrayList;
import java.util.LinkedHashSet;
import java.util.List;
import java.util.Set;
import java.util.UUID;
import java.util.concurrent.TimeUnit;
import java.util.regex.Matcher;
import java.util.regex.Pattern;

/**
 * WebSocket transport between ChatBDI and the Unity chat connector.
 *
 * Unity owns the WebSocketChannel server; ChatBDI connects to it with the
 * WsClient implementation already used by the other JaCaMo elements.
 */
public final class ChatBdiWebSocketClient implements ChatBdiResponseListener, AutoCloseable {

    private static final String CHAT_MESSAGE = "chatMessage";
    private static final String CHAT_RESPONSE = "chatResponse";
    private static final Pattern RECEIVER_PATTERN = Pattern.compile("@([A-Za-z0-9_]+)");

    private final Interpreter interpreter;
    private final WsClient client;
    private volatile boolean running;

    public ChatBdiWebSocketClient(Interpreter interpreter, String host, int port) throws Exception {
        this.interpreter = interpreter;
        this.client = new WsClient(new URI("ws://" + host + ":" + port));
        this.client.setMsgHandler(this::handleIncomingMessage);
    }

    public void start() {
        if (running) {
            return;
        }
        running = true;
        Thread connector = new Thread(() -> {
            boolean firstAttempt = true;
            while (running && !client.isOpen()) {
                try {
                    boolean connected = firstAttempt
                            ? client.connectBlocking(3, TimeUnit.SECONDS)
                            : client.reconnectBlocking();
                    firstAttempt = false;
                    if (connected) {
                        interpreter.logInfo("[CHATBDI] WebSocket connected to " + client.getURI());
                        return;
                    }
                } catch (InterruptedException ex) {
                    Thread.currentThread().interrupt();
                    return;
                } catch (RuntimeException ex) {
                    interpreter.logWarning("[CHATBDI] WebSocket connection failed: " + ex.getMessage());
                }
                try {
                    Thread.sleep(1000);
                } catch (InterruptedException ex) {
                    Thread.currentThread().interrupt();
                    return;
                }
            }
        }, "ChatBDI-WebSocket-Connector");
        connector.setDaemon(true);
        connector.start();
        interpreter.logInfo("[CHATBDI] WebSocket connection started on ws://" + client.getURI());
    }

    private void handleIncomingMessage(String json) {
        final WsMessage message;
        try {
            message = ObjectMapperUtils.convertJsonStringToObject(json, new TypeReference<WsMessage>() { });
        } catch (RuntimeException ex) {
            interpreter.logWarning("[CHATBDI] Ignoring malformed WebSocket message: " + ex.getMessage());
            return;
        }

        // The shared channel sends this message as soon as the connection opens.
        if (!CHAT_MESSAGE.equals(message.getMessageType())) {
            return;
        }

        String text = message.getMessagePayload();
        if (text == null || text.trim().isEmpty()) {
            return;
        }

        List<String> receivers = extractReceivers(text);
        if (receivers.isEmpty() && message.getAgentName() != null && !message.getAgentName().isBlank()) {
            receivers.add(message.getAgentName().trim());
        }
        String plainText = stripReceiverTags(text);
        if (plainText.isEmpty()) {
            return;
        }

        Thread handler = new Thread(() -> {
            try {
                interpreter.handleUserMsg(UUID.randomUUID(), receivers, plainText);
            } catch (ParseException ex) {
                interpreter.logSevere("[CHATBDI] Cannot parse voice message: " + ex.getMessage());
            } catch (Exception ex) {
                interpreter.logSevere("[CHATBDI] Cannot handle voice message: " + ex.getMessage());
            }
        }, "ChatBDI-WebSocket-Message");
        handler.setDaemon(true);
        handler.start();
    }

    private List<String> extractReceivers(String text) {
        Set<String> receivers = new LinkedHashSet<>();
        Matcher matcher = RECEIVER_PATTERN.matcher(text);
        while (matcher.find()) {
            receivers.add(matcher.group(1));
        }
        return new ArrayList<>(receivers);
    }

    private String stripReceiverTags(String text) {
        return text.replaceAll("@([A-Za-z0-9_]+)", "").replaceAll("\\s+", " ").trim();
    }

    @Override
    public void onAgentResponse(String agent, String message) {
        if (message == null || !client.isOpen()) {
            interpreter.logWarning("[CHATBDI] WebSocket is not connected; response was not sent.");
            return;
        }
        WsMessage response = new WsMessage(CHAT_RESPONSE, message, null, agent, null);
        client.send(ObjectMapperUtils.convertIntoJsonString(response));
    }

    @Override
    public void close() {
        running = false;
        client.close();
    }
}
