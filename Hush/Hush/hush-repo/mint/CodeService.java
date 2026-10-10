import com.sun.net.httpserver.HttpExchange;
import com.sun.net.httpserver.HttpServer;
import javax.crypto.Mac;
import javax.crypto.spec.SecretKeySpec;
import java.net.InetSocketAddress;
import java.nio.charset.StandardCharsets;
import java.security.MessageDigest;
import java.security.SecureRandom;
import java.util.HexFormat;
import java.util.Set;
import java.util.concurrent.Executors;
import java.util.regex.Pattern;

public class CodeService {
  static final byte[] KEY = System.getenv().getOrDefault("FIZZ_KEY", "change-me").getBytes(StandardCharsets.UTF_8);
  static final String ADMIN = System.getenv("FIZZ_ADMIN"); 
  static final Set<String> FLAVORS = Set.of("ECL", "STA", "GHO");
  static final Pattern CODE = Pattern.compile("^([A-Z]{3})-([0-9A-F]{6})-([0-9A-F]{6})$");
  static final SecureRandom RNG = new SecureRandom();

  static String sig(String body) throws Exception {
    Mac m = Mac.getInstance("HmacSHA256");
    m.init(new SecretKeySpec(KEY, "HmacSHA256"));
    return HexFormat.of().formatHex(m.doFinal(body.getBytes(StandardCharsets.UTF_8))).substring(0, 6).toUpperCase();
  }
  static String param(HttpExchange x) {
    String q = x.getRequestURI().getQuery();
    return q == null || !q.contains("=") ? "" : q.substring(q.indexOf('=') + 1).trim().toUpperCase();
  }
  static void send(HttpExchange x, int status, String json) throws Exception {
    byte[] b = json.getBytes(StandardCharsets.UTF_8);
    x.getResponseHeaders().add("Content-Type", "application/json");
    x.sendResponseHeaders(status, b.length);
    x.getResponseBody().write(b);
    x.close();
  }
  public static void main(String[] a) throws Exception {
    HttpServer s = HttpServer.create(new InetSocketAddress(8081), 0);
    s.setExecutor(Executors.newFixedThreadPool(8));
    s.createContext("/validate", x -> { try {
      var m = CODE.matcher(param(x));
      boolean ok = m.matches() && FLAVORS.contains(m.group(1))
          && MessageDigest.isEqual(sig(m.group(1) + m.group(2)).getBytes(StandardCharsets.UTF_8), m.group(3).getBytes(StandardCharsets.UTF_8));
      send(x, 200, "{\"valid\":" + ok + ",\"flavor\":\"" + (ok ? m.group(1) : "") + "\"}");
    } catch (Exception e) { try { send(x, 500, "{\"valid\":false,\"flavor\":\"\"}"); } catch (Exception ignored) { x.close(); } } });
    s.createContext("/mint", x -> { try {
      String t = x.getRequestHeaders().getFirst("X-Admin-Token");
      if (ADMIN == null || ADMIN.isEmpty()) { send(x, 404, "{}"); return; }
      if (t == null || !MessageDigest.isEqual(t.getBytes(StandardCharsets.UTF_8), ADMIN.getBytes(StandardCharsets.UTF_8))) { send(x, 403, "{}"); return; }
      String f = param(x);
      if (!FLAVORS.contains(f)) { send(x, 400, "{\"error\":\"flavor must be ECL, STA or GHO\"}"); return; }
      byte[] r = new byte[3]; RNG.nextBytes(r);
      String serial = HexFormat.of().formatHex(r).toUpperCase();
      send(x, 200, "{\"code\":\"" + f + "-" + serial + "-" + sig(f + serial) + "\"}");
    } catch (Exception e) { x.close(); } });
    s.start();
    System.out.println("Mint online :8081");
  }
}
