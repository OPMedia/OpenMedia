using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace MediaBrowser.Model.Cryptography;

/// <summary>
/// A versioned password hash: $PBKDF2-SHA512$iterations$salt-base64$hash-base64.
/// Legacy unsalted SHA1 values (40 hex digits) can also be parsed and verified.
/// </summary>
public sealed class PasswordHash
{
	/// <summary>The algorithm identifier stored in every new hash.</summary>
	public const string Identifier = "PBKDF2-SHA512";

	/// <summary>The prefix stored at the beginning of every new hash.</summary>
	public const string Prefix = "$" + Identifier + "$";

	/// <summary>OWASP recommendation for PBKDF2-SHA512.</summary>
	public const int DefaultIterations = 210000;

	private const int SaltSize = 16;
	private const int KeySize = 64;
	private const int LegacySha1HexLength = 40;

	private readonly byte[] _salt;
	private readonly byte[] _hash;
	private readonly string _encoded;

	public int Iterations { get; }

	public bool IsLegacy { get; }

	/// <summary>The embedded salt (empty for legacy values).</summary>
	public ReadOnlySpan<byte> Salt => _salt;

	private PasswordHash(int iterations, byte[] salt, byte[] hash, bool isLegacy, string encoded)
	{
		Iterations = iterations;
		_salt = salt;
		_hash = hash;
		IsLegacy = isLegacy;
		_encoded = encoded;
	}

	/// <summary>Creates a hash with a fresh random salt.</summary>
	public static PasswordHash Create(ICryptoProvider crypto, string password)
	{
		return Create(crypto, password, DefaultIterations, RandomNumberGenerator.GetBytes(SaltSize));
	}

	/// <summary>Creates a hash with an explicit salt and iteration count (also used by tests).</summary>
	public static PasswordHash Create(ICryptoProvider crypto, string password, int iterations, byte[] salt)
	{
		if (crypto == null)
		{
			throw new ArgumentNullException(nameof(crypto));
		}

		if (password == null)
		{
			throw new ArgumentNullException(nameof(password));
		}

		if (salt == null)
		{
			throw new ArgumentNullException(nameof(salt));
		}

		if (iterations <= 0)
		{
			throw new ArgumentOutOfRangeException(nameof(iterations));
		}

		return new PasswordHash(iterations, salt, crypto.CreatePasswordHash(password, salt, iterations), false, null);
	}

	/// <summary>Parses either a versioned hash or a legacy 40-digit SHA1 hex string.</summary>
	public static bool TryParse(string value, out PasswordHash result)
	{
		result = null;

		if (string.IsNullOrEmpty(value))
		{
			return false;
		}

		if (value[0] != '$')
		{
			if (value.Length != LegacySha1HexLength || !IsHex(value))
			{
				return false;
			}

			result = new PasswordHash(0, Array.Empty<byte>(), Convert.FromHexString(value), true, value);
			return true;
		}

		var parts = value.Split('$');
		if (parts.Length != 5 || parts[0].Length != 0 || !string.Equals(parts[1], Identifier, StringComparison.Ordinal))
		{
			return false;
		}

		if (!int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var iterations) || iterations <= 0)
		{
			return false;
		}

		byte[] salt;
		byte[] hash;
		try
		{
			salt = Convert.FromBase64String(parts[3]);
			hash = Convert.FromBase64String(parts[4]);
		}
		catch (FormatException)
		{
			return false;
		}

		if (salt.Length == 0 || hash.Length == 0)
		{
			return false;
		}

		result = new PasswordHash(iterations, salt, hash, false, value);
		return true;
	}

	/// <summary>Verifies a plaintext credential in constant time.</summary>
	public bool Verify(ICryptoProvider crypto, string password)
	{
		if (crypto == null)
		{
			throw new ArgumentNullException(nameof(crypto));
		}

		password ??= string.Empty;

		var candidate = IsLegacy
			? crypto.ComputeSHA1(Encoding.UTF8.GetBytes(password))
			: crypto.CreatePasswordHash(password, _salt, Iterations);

		return CryptographicOperations.FixedTimeEquals(candidate, _hash);
	}

	/// <summary>
	/// Verifies a client-supplied SHA1 hash string (Emby's legacy "Password" field) in constant time.
	/// Only legacy stored hashes can be verified this way: SHA1 is not reversible, so a versioned
	/// hash cannot be derived from a legacy hash string without the plaintext.
	/// </summary>
	public bool VerifyHashString(ICryptoProvider crypto, string hashString)
	{
		if (crypto == null)
		{
			throw new ArgumentNullException(nameof(crypto));
		}

		if (string.IsNullOrEmpty(hashString))
		{
			return false;
		}

		var normalized = hashString.Replace("-", string.Empty);

		if (IsLegacy)
		{
			if (!TryParse(normalized, out var storedCandidate) || !storedCandidate.IsLegacy)
			{
				return false;
			}

			return CryptographicOperations.FixedTimeEquals(storedCandidate._hash, _hash);
		}

		// An empty password is still represented by SHA1("") for legacy clients.
		if (TryParse(normalized, out var legacyCandidate) && legacyCandidate.IsLegacy
			&& CryptographicOperations.FixedTimeEquals(legacyCandidate._hash, crypto.ComputeSHA1(Array.Empty<byte>()))
			&& Verify(crypto, string.Empty))
		{
			return true;
		}

		// A versioned hash may have been created from a client-supplied hash string.
		return Verify(crypto, normalized);
	}

	/// <summary>True when the value represents no password configured.</summary>
	public static bool IsEmpty(ICryptoProvider crypto, string value)
	{
		if (string.IsNullOrEmpty(value))
		{
			return true;
		}

		return TryParse(value, out var parsed) && parsed.Verify(crypto, string.Empty);
	}

	public override string ToString()
	{
		if (_encoded != null)
		{
			return _encoded;
		}

		return string.Concat(
			Prefix,
			Iterations.ToString(CultureInfo.InvariantCulture),
			"$",
			Convert.ToBase64String(_salt),
			"$",
			Convert.ToBase64String(_hash));
	}

	private static bool IsHex(string value)
	{
		foreach (var c in value)
		{
			if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F')))
			{
				return false;
			}
		}

		return true;
	}
}
