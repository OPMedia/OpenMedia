using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using MediaBrowser.Model.Extensions;

namespace MediaBrowser.Model.Services;

public static class MyHttpUtility
{
	private static readonly long[] entities = new long[253]
	{
		4703284585813770240L, 4711156041321349120L, 4711725575167803392L, 4712861297990238208L, 4714266503556366336L, 4715947682705702912L, 4716510624025477120L, 4716796495364358144L, 4784358139111669760L, 4855836305175347200L,
		4857247646839996416L, 4927333161101295616L, 4928464614326272000L, 4995697051497922560L, 4999386417473060864L, 4999955951319515136L, 5001091674141949952L, 5003626082636623360L, 5004731738543357952L, 5005026871516069888L,
		5143512565980069888L, 5287616793624772608L, 5288186327471226880L, 5289322050293661696L, 5291576047144271872L, 5293257247667781632L, 5431746253551566848L, 5503800488981757952L, 5581367313195597824L, 5653259346518540288L,
		5653424907233525760L, 5712090902344761344L, 5719962357852340224L, 5720531891698794496L, 5721667614521229312L, 5723342196141195264L, 5723346577300352512L, 5725038717121855488L, 5725316940556468224L, 5725602811895349248L,
		5793996369333059584L, 5794162395588853760L, 5796811588946100224L, 5797092594076876800L, 5938118154478682112L, 6008753471966019584L, 6010448897179123712L, 6073191312423649280L, 6080269614787330048L, 6082222847281856512L,
		6152307922079907840L, 6152877455926362112L, 6154013178748796928L, 6156547587243470336L, 6157948376122916864L, 6370623147892277248L, 6440538298231619584L, 6446178752274628608L, 6513740396021940224L, 7016999050535043072L,
		7017568584381497344L, 7017581787144519680L, 7018134794282205184L, 7018704307203932160L, 7020097409862167808L, 7020109512770060288L, 7020390539442782208L, 7020658820279959552L, 7020662118814842880L, 7021234358782525440L,
		7021790691919396864L, 7022089754938179584L, 7022353633239171072L, 7022639504578052096L, 7089916462575386624L, 7090201148325363712L, 7093862527975686144L, 7094695999104352256L, 7161128027798110208L, 7161679314389041152L,
		7162241186348924928L, 7162252226897903616L, 7163090656053690368L, 7163382451836813312L, 7164230172936241152L, 7165066920830435328L, 7165069197163102208L, 7165897101266649088L, 7166757527332323328L, 7166760217683558400L,
		7224181111230824448L, 7233176170314989568L, 7233188310485565440L, 7234301626138230784L, 7234307623539965952L, 7235421399056121856L, 7235444471375396864L, 7305229426686754816L, 7305798960533209088L, 7306934683355643904L,
		7308621415840743424L, 7308624695165714432L, 7308906170142425088L, 7309469091850317312L, 7309752766010753024L, 7310574747757051904L, 7310582444338446336L, 7310869880729763840L, 7310875391172804608L, 7311709939624312832L,
		7380959323184168960L, 7381244077039943680L, 7382069817868681216L, 7382069817868812288L, 7382069817902366720L, 7382069887574736896L, 7449355575193763840L, 7450361158554353664L, 7454583283205013504L, 7512411487382536192L,
		7521418686637277184L, 7522525896800141312L, 7522537965473497088L, 7593459802838466560L, 7594029336684920832L, 7594608715039244288L, 7595165059507355648L, 7596835243147919360L, 7597122224423698432L, 7597137164769427456L,
		7597419056357965824L, 7597983124939866112L, 7598532917471477760L, 7599100256881475584L, 7737589262765260800L, 7800641863534247936L, 7809643498195451904L, 7809644617497837568L, 7809647978024534016L, 7809649062788988928L,
		7810197682248482816L, 7810492402954665984L, 7810649128743993344L, 7811049829587615744L, 7813595138943614976L, 7813598018929688576L, 7814428150208659456L, 7814696918347350016L, 7814714527605325824L, 7814871253394653184L,
		7881690164152500224L, 7882532396099174400L, 7883941965828456448L, 7883943005218144256L, 7883954073408372736L, 7887210322409291776L, 7953746634536386560L, 7954046816763248640L, 7954589990137102336L, 7954764316819849216L,
		7955890216726691840L, 7957706609935777792L, 7957707062752837632L, 7958834030261043200L, 7959102355732234240L, 7959267916447219712L, 8025805367066034176L, 8026374900912488448L, 8026941110813196288L, 8027510623734923264L,
		8028908158556569600L, 8029185205354889216L, 8029189586514046464L, 8030037387297947648L, 8030481085555015680L, 8030591474804457472L, 8030591504869228544L, 8030881726335549440L, 8031159949770162176L, 8031159954082824192L,
		8031445821109043200L, 8097879365926256640L, 8097879447530635264L, 8099005319141392384L, 8099005330257608704L, 8099839378546753536L, 8100005404802547712L, 8100135147174625280L, 8100978968350294016L, 8101823371647385600L,
		8102654598159794176L, 8102661154880356352L, 8102661206419963904L, 8102935603290570752L, 8175563242567892992L, 8232987427761815552L, 8241979196860006400L, 8241990181725405184L, 8241993542252101632L, 8241994627016556544L,
		8242543246476050432L, 8242837967182233600L, 8243101809455923200L, 8243107942669221888L, 8243395393815183360L, 8243961163692376064L, 8245084864575963136L, 8247042482574917632L, 8247060091832893440L, 8314332611266740224L,
		8314596481179713536L, 8314893356039667712L, 8315161636876845056L, 8316029752846581760L, 8316291906392817664L, 8316291906399502336L, 8316298033683759104L, 8318255595579965440L, 8319663638776381440L, 8319664072568078336L,
		8319675733404286976L, 8319679031939170304L, 8319679242392567808L, 8319679246687535104L, 8319679250982502400L, 8319679465730867200L, 8321082461475831808L, 8386112624001024000L, 8388065847976132608L, 8388065856495550464L,
		8388065856503118189L, 8388070229081587712L, 8388076843239997440L, 8388354959401287680L, 8388356063442763776L, 8390876139563778048L, 8449160209875599360L, 8458150931293601792L, 8458167409130340352L, 8458720465140056064L,
		8459856187962490880L, 8461538022154829824L, 8462390596382752768L, 8462390596457164288L, 8463791385336610816L, 8603398547593756672L, 8676466157105971200L, 8746381307445313536L, 8747518797516111872L, 8752021761488322560L,
		8819583405235634176L, 8824638543088320512L, 8824643396401364992L
	};

	private static readonly char[] entities_values = new char[253]
	{
		'Æ', 'Á', 'Â', 'À', 'Α', 'Å', 'Ã', 'Ä', 'Β', 'Ç',
		'Χ', '‡', 'Δ', 'Ð', 'É', 'Ê', 'È', 'Ε', 'Η', 'Ë',
		'Γ', 'Í', 'Î', 'Ì', 'Ι', 'Ï', 'Κ', 'Λ', 'Μ', 'Ñ',
		'Ν', 'Œ', 'Ó', 'Ô', 'Ò', 'Ω', 'Ο', 'Ø', 'Õ', 'Ö',
		'Φ', 'Π', '″', 'Ψ', 'Ρ', 'Š', 'Σ', 'Þ', 'Τ', 'Θ',
		'Ú', 'Û', 'Ù', 'Υ', 'Ü', 'Ξ', 'Ý', 'Ÿ', 'Ζ', 'á',
		'â', '\u00b4', 'æ', 'à', 'ℵ', 'α', '&', '∧', '∠', '\'',
		'å', '≈', 'ã', 'ä', '„', 'β', '¦', '•', '∩', 'ç',
		'\u00b8', '¢', 'χ', 'ˆ', '♣', '≅', '©', '↵', '∪', '¤',
		'⇓', '†', '↓', '°', 'δ', '♦', '÷', 'é', 'ê', 'è',
		'∅', '\u2003', '\u2002', 'ε', '≡', 'η', 'ð', 'ë', '€', '∃',
		'ƒ', '∀', '½', '¼', '¾', '⁄', 'γ', '≥', '>', '⇔',
		'↔', '♥', '…', 'í', 'î', '¡', 'ì', 'ℑ', '∞', '∫',
		'ι', '¿', '∈', 'ï', 'κ', '⇐', 'λ', '〈', '«', '←',
		'⌈', '“', '≤', '⌊', '∗', '◊', '\u200e', '‹', '‘', '<',
		'\u00af', '—', 'µ', '·', '−', 'μ', '∇', '\u00a0', '–', '≠',
		'∋', '¬', '∉', '⊄', 'ñ', 'ν', 'ó', 'ô', 'œ', 'ò',
		'‾', 'ω', 'ο', '⊕', '∨', 'ª', 'º', 'ø', 'õ', '⊗',
		'ö', '¶', '∂', '‰', '⊥', 'φ', 'π', 'ϖ', '±', '£',
		'′', '∏', '∝', 'ψ', '"', '⇒', '√', '〉', '»', '→',
		'⌉', '”', 'ℜ', '®', '⌋', 'ρ', '\u200f', '›', '’', '‚',
		'š', '⋅', '§', '\u00ad', 'σ', 'ς', '∼', '♠', '⊂', '⊆',
		'∑', '⊃', '¹', '²', '³', '⊇', 'ß', 'τ', '∴', 'θ',
		'ϑ', '\u2009', 'þ', '\u02dc', '×', '™', '⇑', 'ú', '↑', 'û',
		'ù', '\u00a8', 'ϒ', 'υ', 'ü', '℘', 'ξ', 'ý', '¥', 'ÿ',
		'ζ', '\u200d', '\u200c'
	};

	private static void WriteCharBytes(IList buf, char ch, Encoding e)
	{
		if (ch > 'ÿ')
		{
			byte[] bytes = e.GetBytes(new char[1] { ch });
			foreach (byte b in bytes)
			{
				buf.Add(b);
			}
		}
		else
		{
			buf.Add((byte)ch);
		}
	}

	public static string UrlDecode(string s, Encoding e)
	{
		if (s == null)
		{
			return null;
		}
		if (s.IndexOf('%') == -1 && s.IndexOf('+') == -1)
		{
			return s;
		}
		if (e == null)
		{
			e = Encoding.UTF8;
		}
		long num = s.Length;
		List<byte> list = new List<byte>();
		for (int i = 0; i < num; i++)
		{
			char c = s[i];
			if (c == '%' && i + 2 < num && s[i + 1] != '%')
			{
				int num2;
				if (s[i + 1] == 'u' && i + 5 < num)
				{
					num2 = GetChar(s, i + 2, 4);
					if (num2 != -1)
					{
						WriteCharBytes(list, (char)num2, e);
						i += 5;
					}
					else
					{
						WriteCharBytes(list, '%', e);
					}
				}
				else if ((num2 = GetChar(s, i + 1, 2)) != -1)
				{
					WriteCharBytes(list, (char)num2, e);
					i += 2;
				}
				else
				{
					WriteCharBytes(list, '%', e);
				}
			}
			else if (c == '+')
			{
				WriteCharBytes(list, ' ', e);
			}
			else
			{
				WriteCharBytes(list, c, e);
			}
		}
		byte[] array = list.ToArray(list.Count);
		list = null;
		return e.GetString(array, 0, array.Length);
	}

	private static int GetInt(byte b)
	{
		char c = (char)b;
		if (c >= '0' && c <= '9')
		{
			return c - 48;
		}
		if (c >= 'a' && c <= 'f')
		{
			return c - 97 + 10;
		}
		if (c >= 'A' && c <= 'F')
		{
			return c - 65 + 10;
		}
		return -1;
	}

	private static int GetChar(string str, int offset, int length)
	{
		int num = 0;
		int num2 = length + offset;
		for (int i = offset; i < num2; i++)
		{
			char c = str[i];
			if (c > '\u007f')
			{
				return -1;
			}
			int num3 = GetInt((byte)c);
			if (num3 == -1)
			{
				return -1;
			}
			num = (num << 4) + num3;
		}
		return num;
	}

	private static bool TryConvertKeyToEntity(string key, out char value)
	{
		long num = CalculateKeyValue(key);
		if (num == 0L)
		{
			value = '\0';
			return false;
		}
		int num2 = Array.BinarySearch(entities, num);
		if (num2 < 0)
		{
			value = '\0';
			return false;
		}
		value = entities_values[num2];
		return true;
	}

	private static long CalculateKeyValue(string s)
	{
		if (s.Length > 8)
		{
			return 0L;
		}
		long num = 0L;
		for (int i = 0; i < s.Length; i++)
		{
			long num2 = s[i];
			if (num2 > 122 || num2 < 48)
			{
				return 0L;
			}
			num |= num2 << (7 - i) * 8;
		}
		return num;
	}

	public static string HtmlDecode(string s)
	{
		if (s == null)
		{
			throw new ArgumentNullException("s");
		}
		if (s.IndexOf('&') == -1)
		{
			return s;
		}
		StringBuilder stringBuilder = new StringBuilder();
		StringBuilder stringBuilder2 = new StringBuilder();
		int length = s.Length;
		int num = 0;
		int num2 = 0;
		int num3 = 0;
		bool flag = false;
		for (int i = 0; i < length; i++)
		{
			char value = s[i];
			if (num == 0)
			{
				if (value == '&')
				{
					stringBuilder.Append(value);
					num = 1;
				}
				else
				{
					stringBuilder2.Append(value);
				}
				continue;
			}
			if (value == '&')
			{
				num = 1;
				if (num3 > 0)
				{
					stringBuilder.Append(s, num3, i - num3);
					num3 = 0;
				}
				stringBuilder2.Append(stringBuilder.ToString());
				stringBuilder.Length = 0;
				stringBuilder.Append('&');
				continue;
			}
			switch (num)
			{
			case 1:
				if (value == ';')
				{
					num = 0;
					stringBuilder2.Append(stringBuilder.ToString());
					stringBuilder2.Append(value);
					stringBuilder.Length = 0;
				}
				else
				{
					num2 = 0;
					flag = false;
					num = ((value == '#') ? 3 : 2);
					stringBuilder.Append(value);
				}
				break;
			case 2:
				stringBuilder.Append(value);
				if (value == ';')
				{
					string text = stringBuilder.ToString();
					num = 0;
					stringBuilder.Length = 0;
					if (text.Length > 1 && TryConvertKeyToEntity(text.Substring(1, text.Length - 2), out value))
					{
						stringBuilder2.Append(value);
					}
					else
					{
						stringBuilder2.Append(text);
					}
				}
				break;
			case 3:
				if (value == ';')
				{
					if (num2 < 65536)
					{
						stringBuilder2.Append((char)num2);
					}
					else
					{
						stringBuilder2.Append((char)(55296 + (num2 - 65536 >> 10)));
						stringBuilder2.Append((char)(56320 + ((num2 - 65536) & 0x3FF)));
					}
					num = 0;
					stringBuilder.Length = 0;
					num3 = 0;
					break;
				}
				if (value == 'x' || (value == 'X' && !flag))
				{
					num3 = i;
					flag = true;
					break;
				}
				if (char.IsDigit(value))
				{
					if (num3 == 0)
					{
						num3 = i;
					}
					num2 = num2 * (flag ? 16 : 10) + (value - 48);
					break;
				}
				if (flag)
				{
					if (value >= 'a' && value <= 'f')
					{
						num2 = num2 * 16 + 10 + (value - 97);
						break;
					}
					if (value >= 'A' && value <= 'F')
					{
						num2 = num2 * 16 + 10 + (value - 65);
						break;
					}
				}
				num = 2;
				if (num3 > 0)
				{
					stringBuilder.Append(s, num3, i - num3);
					num3 = 0;
				}
				stringBuilder.Append(value);
				break;
			}
		}
		if (stringBuilder.Length > 0)
		{
			stringBuilder2.Append((object?)stringBuilder);
		}
		else if (num3 > 0)
		{
			stringBuilder2.Append(s, num3, s.Length - num3);
		}
		return stringBuilder2.ToString();
	}

	public static QueryParamCollection ParseQueryString(string query)
	{
		return ParseQueryString(query, Encoding.UTF8);
	}

	public static QueryParamCollection ParseQueryString(string query, Encoding encoding)
	{
		if (query == null)
		{
			throw new ArgumentNullException("query");
		}
		if (encoding == null)
		{
			throw new ArgumentNullException("encoding");
		}
		if (query.Length == 0 || (query.Length == 1 && query[0] == '?'))
		{
			return new QueryParamCollection();
		}
		if (query[0] == '?')
		{
			query = query.Substring(1);
		}
		QueryParamCollection result = new QueryParamCollection();
		ParseQueryString(query, encoding, result);
		return result;
	}

	internal static void ParseQueryString(string query, Encoding encoding, QueryParamCollection result)
	{
		if (query.Length == 0)
		{
			return;
		}
		string text = HtmlDecode(query);
		int length = text.Length;
		int num = 0;
		bool flag = true;
		while (num <= length)
		{
			int num2 = -1;
			int num3 = -1;
			for (int i = num; i < length; i++)
			{
				if (num2 == -1 && text[i] == '=')
				{
					num2 = i + 1;
				}
				else if (text[i] == '&')
				{
					num3 = i;
					break;
				}
			}
			if (flag)
			{
				flag = false;
				if (text[num] == '?')
				{
					num++;
				}
			}
			string key;
			if (num2 == -1)
			{
				key = null;
				num2 = num;
			}
			else
			{
				key = UrlDecode(text.Substring(num, num2 - num - 1), encoding);
			}
			if (num3 < 0)
			{
				num = -1;
				num3 = text.Length;
			}
			else
			{
				num = num3 + 1;
			}
			string value = UrlDecode(text.Substring(num2, num3 - num2), encoding);
			result.Add(key, value);
			if (num == -1)
			{
				break;
			}
		}
	}
}
