package gg.hulla.social;

import android.app.Activity;
import android.database.Cursor;
import android.provider.ContactsContract;
import org.json.JSONArray;

/** The address book's phone numbers for NativeContacts (people you may know), once READ_CONTACTS is granted. */
public final class HullaContacts {
    /** Answers once, on a background thread: the numbers as a JSON array, or an error code. */
    public interface Callback {
        void onNumbers(String json);

        void onError(String code);
    }

    private HullaContacts() {
    }

    public static void read(final Activity activity, final Callback callback) {
        new Thread(new Runnable() {
            @Override
            public void run() {
                JSONArray numbers = new JSONArray();
                String[] columns = { ContactsContract.CommonDataKinds.Phone.NUMBER };
                try (Cursor cursor = activity.getContentResolver().query(
                    ContactsContract.CommonDataKinds.Phone.CONTENT_URI, columns, null, null, null)) {
                    if (cursor != null) {
                        while (cursor.moveToNext()) {
                            String number = cursor.getString(0);
                            if (number != null) {
                                numbers.put(number);
                            }
                        }
                    }
                    callback.onNumbers(numbers.toString());
                } catch (SecurityException error) {
                    callback.onError("contacts.denied");
                } catch (Exception error) {
                    callback.onError("contacts.unavailable");
                }
            }
        }).start();
    }
}
