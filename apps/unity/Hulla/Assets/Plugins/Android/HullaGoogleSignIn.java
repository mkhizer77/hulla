package gg.hulla.social;

import android.app.Activity;
import android.os.CancellationSignal;
import androidx.credentials.Credential;
import androidx.credentials.CredentialManager;
import androidx.credentials.CredentialManagerCallback;
import androidx.credentials.CustomCredential;
import androidx.credentials.GetCredentialRequest;
import androidx.credentials.GetCredentialResponse;
import androidx.credentials.exceptions.GetCredentialCancellationException;
import androidx.credentials.exceptions.GetCredentialException;
import com.google.android.libraries.identity.googleid.GetSignInWithGoogleOption;
import com.google.android.libraries.identity.googleid.GoogleIdTokenCredential;
import java.util.concurrent.Executors;

/** Sign in with Google on Android through Credential Manager (GDD 10.2, #38): the account picker, then an ID token. */
public final class HullaGoogleSignIn {
    /** Answers once, on a background thread: the ID token, or an error code (auth.cancelled, auth.provider_unavailable). */
    public interface Callback {
        void onToken(String token);

        void onError(String code);
    }

    private HullaGoogleSignIn() {
    }

    public static void signIn(final Activity activity, final String webClientId, final Callback callback) {
        GetSignInWithGoogleOption option = new GetSignInWithGoogleOption.Builder(webClientId).build();
        GetCredentialRequest request = new GetCredentialRequest.Builder().addCredentialOption(option).build();
        CredentialManager manager = CredentialManager.create(activity);
        manager.getCredentialAsync(activity, request, new CancellationSignal(), Executors.newSingleThreadExecutor(),
            new CredentialManagerCallback<GetCredentialResponse, GetCredentialException>() {
                @Override
                public void onResult(GetCredentialResponse response) {
                    Credential credential = response.getCredential();
                    if (credential instanceof CustomCredential
                        && GoogleIdTokenCredential.TYPE_GOOGLE_ID_TOKEN_CREDENTIAL.equals(credential.getType())) {
                        try {
                            callback.onToken(GoogleIdTokenCredential.createFrom(((CustomCredential) credential).getData()).getIdToken());
                            return;
                        } catch (Exception error) {
                            // Falls through to the error below.
                        }
                    }

                    callback.onError("auth.provider_unavailable");
                }

                @Override
                public void onError(GetCredentialException error) {
                    callback.onError(error instanceof GetCredentialCancellationException ? "auth.cancelled" : "auth.provider_unavailable");
                }
            });
    }
}
