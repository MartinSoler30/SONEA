package com.etitc.sonea.ocr;

import android.graphics.Bitmap;
import android.graphics.Rect;

import com.google.mlkit.vision.common.InputImage;
import com.google.mlkit.vision.text.Text;
import com.google.mlkit.vision.text.TextRecognition;
import com.google.mlkit.vision.text.TextRecognizer;
import com.google.mlkit.vision.text.latin.TextRecognizerOptions;

import org.json.JSONArray;
import org.json.JSONObject;

/**
 * Lector de letreros de puerta con ML Kit.
 *
 * Recibe un frame RGBA ya convertido por Unity, lo pasa a un Bitmap ARGB y
 * devuelve el texto reconocido en JSON. Todo ocurre en el dispositivo: no se
 * envia ninguna imagen a internet.
 */
public final class DoorSignOcr {

    public interface OcrCallback {
        void onResult(String json);
    }

    private static final TextRecognizer RECOGNIZER =
        TextRecognition.getClient(TextRecognizerOptions.DEFAULT_OPTIONS);

    private DoorSignOcr() {
    }

    /**
     * @param argb            pixeles en formato ARGB_8888
     * @param width           ancho del frame
     * @param height          alto del frame
     * @param rotationDegrees giro que hay que aplicar antes de leer (0, 90, 180 o 270)
     * @param callback        recibe {"text":"...","lines":[...]} o {"error":"..."}
     */
    public static void recognizeAsync(int[] argb, int width, int height,
                                      int rotationDegrees, OcrCallback callback) {
        if (argb == null || width <= 0 || height <= 0) {
            callback.onResult("{\"error\":\"Frame vacio\"}");
            return;
        }

        Bitmap bitmap;
        try {
            bitmap = Bitmap.createBitmap(width, height, Bitmap.Config.ARGB_8888);
            bitmap.setPixels(argb, 0, width, 0, 0, width, height);
        } catch (Throwable error) {
            callback.onResult("{\"error\":\"" + escape(error.getMessage()) + "\"}");
            return;
        }

        InputImage image = InputImage.fromBitmap(bitmap, rotationDegrees);
        RECOGNIZER.process(image)
            .addOnSuccessListener(result -> {
                try {
                    callback.onResult(toJson(result));
                } catch (Throwable error) {
                    callback.onResult("{\"error\":\"" + escape(error.getMessage()) + "\"}");
                } finally {
                    bitmap.recycle();
                }
            })
            .addOnFailureListener(error -> {
                try {
                    callback.onResult("{\"error\":\"" + escape(error.getMessage()) + "\"}");
                } finally {
                    bitmap.recycle();
                }
            });
    }

    private static String toJson(Text result) throws Exception {
        JSONObject root = new JSONObject();
        root.put("text", result.getText());

        JSONArray lines = new JSONArray();
        for (Text.TextBlock block : result.getTextBlocks()) {
            for (Text.Line line : block.getLines()) {
                JSONObject item = new JSONObject();
                item.put("text", line.getText());
                Rect box = line.getBoundingBox();
                if (box != null) {
                    item.put("left", box.left);
                    item.put("top", box.top);
                    item.put("right", box.right);
                    item.put("bottom", box.bottom);
                }
                lines.put(item);
            }
        }
        root.put("lines", lines);
        return root.toString();
    }

    private static String escape(String value) {
        if (value == null) return "OCR fallo";
        return value.replace("\\", "\\\\").replace("\"", "\\\"");
    }
}
