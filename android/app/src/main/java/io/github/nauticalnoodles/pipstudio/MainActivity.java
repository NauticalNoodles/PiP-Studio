package io.github.nauticalnoodles.pipstudio;

import android.app.Activity;
import android.app.AlertDialog;
import android.content.SharedPreferences;
import android.os.Bundle;
import android.text.InputType;
import android.webkit.WebResourceError;
import android.webkit.WebResourceRequest;
import android.webkit.WebSettings;
import android.webkit.WebView;
import android.webkit.WebViewClient;
import android.widget.EditText;
import android.widget.FrameLayout;

/**
 * PiP Studio's phone companion as an app: it asks for your PC's address once, then opens the companion page that
 * PiP Studio serves there, full screen. Pairing and everything else happen in that page, exactly as in a browser.
 * If the PC can't be reached, it asks for the address again.
 */
public class MainActivity extends Activity {
    static final int PORT = 48765;
    WebView web;
    SharedPreferences prefs;
    boolean asking;

    @Override
    protected void onCreate(Bundle saved) {
        super.onCreate(saved);
        prefs = getSharedPreferences("pipstudio", MODE_PRIVATE);
        web = new WebView(this);
        web.setBackgroundColor(0xFF0A0A0C);
        setContentView(web);
        WebSettings s = web.getSettings();
        s.setJavaScriptEnabled(true);
        s.setDomStorageEnabled(true);                 // the page keeps this phone's pairing here
        s.setAllowFileAccess(false);
        s.setAllowContentAccess(false);
        web.setWebViewClient(new WebViewClient() {
            @Override
            public boolean shouldOverrideUrlLoading(WebView v, WebResourceRequest r) {
                return !r.getUrl().toString().startsWith(address());   // stay on the PC's page
            }

            @Override
            public void onReceivedError(WebView v, WebResourceRequest r, WebResourceError e) {
                if (r.isForMainFrame()) askForAddress(true);
            }
        });
        if (saved != null) web.restoreState(saved);
        else if (address().isEmpty()) askForAddress(false);
        else web.loadUrl(address());
    }

    String address() { return prefs.getString("address", ""); }

    /** "192.168.0.82" or "192.168.0.82:48765" or "http://..." → http://192.168.0.82:48765/ */
    static String normalize(String text) {
        String a = text.trim();
        if (a.isEmpty()) return "";
        if (!a.startsWith("http://") && !a.startsWith("https://")) a = "http://" + a;
        int hostStart = a.indexOf("//") + 2;
        int slash = a.indexOf('/', hostStart);
        String host = slash < 0 ? a.substring(hostStart) : a.substring(hostStart, slash);
        if (!host.contains(":")) host = host + ":" + PORT;
        return a.substring(0, hostStart) + host + "/";
    }

    void askForAddress(boolean failed) {
        if (asking || isFinishing()) return;
        asking = true;
        EditText input = new EditText(this);
        input.setInputType(InputType.TYPE_CLASS_TEXT | InputType.TYPE_TEXT_VARIATION_URI);
        input.setHint("192.168.0.82");
        String current = address();
        if (!current.isEmpty()) input.setText(current.replace("http://", "").replace(":" + PORT + "/", "").replace("/", ""));
        FrameLayout box = new FrameLayout(this);
        int pad = (int) (20 * getResources().getDisplayMetrics().density);
        box.setPadding(pad, pad / 2, pad, 0);
        box.addView(input);
        new AlertDialog.Builder(this)
                .setTitle(failed ? "Can't reach PiP Studio" : "Your PC's address")
                .setMessage((failed ? "Check that PiP Studio is running and this phone is on the same Wi-Fi. " : "")
                        + "Type the address PiP Studio shows in Settings > General > Phone companion > Pair a phone.")
                .setView(box)
                .setCancelable(false)
                .setPositiveButton("Connect", (d, w) -> {
                    asking = false;
                    String url = normalize(input.getText().toString());
                    if (url.isEmpty()) { askForAddress(false); return; }
                    prefs.edit().putString("address", url).apply();
                    web.loadUrl(url);
                })
                .show();
    }

    @Override
    protected void onSaveInstanceState(Bundle out) {
        super.onSaveInstanceState(out);
        web.saveState(out);
    }

    @Override
    public void onBackPressed() {
        if (web.canGoBack()) web.goBack(); else super.onBackPressed();
    }
}
