package com.nightwire.securestore;

import android.content.Context;
import android.content.SharedPreferences;
import android.security.keystore.KeyGenParameterSpec;
import android.security.keystore.KeyProperties;
import android.util.Base64;

import java.nio.ByteBuffer;
import java.security.KeyStore;
import java.util.UUID;
import java.util.Enumeration;

import javax.crypto.Cipher;
import javax.crypto.KeyGenerator;
import javax.crypto.SecretKey;
import javax.crypto.spec.GCMParameterSpec;

/**
 * Stores the accepted release-catalogue sequence encrypted with a rotating StrongBox key.
 *
 * Each successful update creates a new StrongBox key, encrypts the sequence, deletes the
 * previous key, and only then commits the new pointer. Restoring an older preferences snapshot
 * therefore points at a key that has already been deleted. If a crash happens between deletion
 * and commit, loading fails closed rather than silently resetting the counter.
 */
public final class NightwireSequenceStore {
    private static final String PREFS = "nightwire_catalogue_sequence_v1";
    private static final String ALIAS_PREFIX = "nightwire.catalogue.sequence.";
    private static final String KEY_ALIAS = "key_alias";
    private static final String IV = "iv";
    private static final String VALUE = "value";
    private static final String AAD = "NIGHTWIRE-CATALOGUE-SEQUENCE-V1";

    private NightwireSequenceStore() { }

    public static synchronized long loadSequence(Context context, String namespace) throws Exception {
        SharedPreferences preferences = preferences(context, namespace);
        String alias = preferences.getString(KEY_ALIAS, null);
        if (alias == null) {
            if (preferences.contains(IV) || preferences.contains(VALUE)) {
                throw new IllegalStateException("Sequence record is incomplete.");
            }
            // A missing preference record is a first launch only when no sequence key
            // has ever been provisioned for this app. If preferences were cleared or
            // restored independently, an orphaned StrongBox key proves this is not a
            // clean installation; fail closed instead of resetting the rollback floor.
            if (hasAnySequenceKey()) {
                throw new IllegalStateException("Sequence preferences are missing while a NIGHTWIRE sequence key still exists.");
            }
            return 0L;
        }
        if (!alias.startsWith(ALIAS_PREFIX) ||
                !alias.substring(ALIAS_PREFIX.length()).matches(
                        "[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}")) {
            throw new IllegalStateException("Sequence key alias is invalid.");
        }

        String encodedIv = preferences.getString(IV, null);
        String encodedValue = preferences.getString(VALUE, null);
        if (encodedIv == null || encodedValue == null) {
            throw new IllegalStateException("Sequence record is incomplete.");
        }

        byte[] iv = Base64.decode(encodedIv, Base64.NO_WRAP);
        byte[] ciphertext = Base64.decode(encodedValue, Base64.NO_WRAP);
        SecretKey key = getKey(alias);
        Cipher cipher = Cipher.getInstance("AES/GCM/NoPadding");
        cipher.init(Cipher.DECRYPT_MODE, key, new GCMParameterSpec(128, iv));
        cipher.updateAAD(aad(namespace, alias));
        byte[] plaintext = cipher.doFinal(ciphertext);
        if (plaintext.length != 8) {
            throw new IllegalStateException("Sequence plaintext has an invalid length.");
        }
        long sequence = ByteBuffer.wrap(plaintext).getLong();
        if (sequence < 0L) {
            throw new IllegalStateException("Stored sequence is negative.");
        }
        return sequence;
    }

    public static synchronized void saveSequence(Context context, String namespace, long sequence) throws Exception {
        if (sequence < 1L) throw new IllegalArgumentException("Sequence must be positive.");
        SharedPreferences preferences = preferences(context, namespace);
        long current = loadSequence(context, namespace);
        if (sequence <= current) {
            if (sequence == current) return;
            throw new IllegalArgumentException("Catalogue sequence cannot move backwards.");
        }

        String oldAlias = preferences.getString(KEY_ALIAS, null);
        String newAlias = ALIAS_PREFIX + UUID.randomUUID().toString();
        SecretKey newKey = createStrongBoxKey(newAlias);
        Cipher cipher = Cipher.getInstance("AES/GCM/NoPadding");
        cipher.init(Cipher.ENCRYPT_MODE, newKey);
        cipher.updateAAD(aad(namespace, newAlias));
        byte[] ciphertext = cipher.doFinal(ByteBuffer.allocate(8).putLong(sequence).array());
        String encodedIv = Base64.encodeToString(cipher.getIV(), Base64.NO_WRAP);
        String encodedValue = Base64.encodeToString(ciphertext, Base64.NO_WRAP);

        // Delete the old hardware key before publishing the new pointer. A crash in this
        // narrow window makes the old record unreadable (fail closed), never rollback-valid.
        if (oldAlias != null) {
            KeyStore keyStore = keyStore();
            keyStore.deleteEntry(oldAlias);
        }

        boolean committed = preferences.edit()
                .putString(KEY_ALIAS, newAlias)
                .putString(IV, encodedIv)
                .putString(VALUE, encodedValue)
                .commit();
        if (!committed) {
            KeyStore keyStore = keyStore();
            keyStore.deleteEntry(newAlias);
            throw new IllegalStateException("Could not durably commit catalogue sequence.");
        }
    }

    private static SharedPreferences preferences(Context context, String namespace) {
        if (context == null) throw new IllegalArgumentException("Context is required.");
        if (namespace == null || !namespace.matches("[A-Za-z0-9_.-]{1,80}")) {
            throw new IllegalArgumentException("Sequence namespace is invalid.");
        }
        return context.getApplicationContext().getSharedPreferences(PREFS + "." + namespace,
                Context.MODE_PRIVATE);
    }

    private static boolean hasAnySequenceKey() throws Exception {
        KeyStore keyStore = keyStore();
        Enumeration<String> aliases = keyStore.aliases();
        while (aliases.hasMoreElements()) {
            if (aliases.nextElement().startsWith(ALIAS_PREFIX)) return true;
        }
        return false;
    }

    private static SecretKey createStrongBoxKey(String alias) throws Exception {
        KeyGenerator generator = KeyGenerator.getInstance(KeyProperties.KEY_ALGORITHM_AES, "AndroidKeyStore");
        KeyGenParameterSpec spec = new KeyGenParameterSpec.Builder(alias,
                KeyProperties.PURPOSE_ENCRYPT | KeyProperties.PURPOSE_DECRYPT)
                .setBlockModes(KeyProperties.BLOCK_MODE_GCM)
                .setEncryptionPaddings(KeyProperties.ENCRYPTION_PADDING_NONE)
                .setKeySize(256)
                .setRandomizedEncryptionRequired(true)
                .setIsStrongBoxBacked(true)
                .build();
        generator.init(spec);
        return generator.generateKey();
    }

    private static SecretKey getKey(String alias) throws Exception {
        KeyStore keyStore = keyStore();
        java.security.Key key = keyStore.getKey(alias, null);
        if (!(key instanceof SecretKey)) {
            throw new IllegalStateException("StrongBox sequence key is missing.");
        }
        return (SecretKey) key;
    }

    private static KeyStore keyStore() throws Exception {
        KeyStore store = KeyStore.getInstance("AndroidKeyStore");
        store.load(null);
        return store;
    }

    private static byte[] aad(String namespace, String alias) {
        return (AAD + ":" + namespace + ":" + alias).getBytes(java.nio.charset.StandardCharsets.UTF_8);
    }
}
