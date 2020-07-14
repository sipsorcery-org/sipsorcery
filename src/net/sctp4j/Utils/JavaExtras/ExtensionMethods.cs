using System.Text;

public static class ExtensionMethods {
	public static byte[] getBytes(this string s) {
		return Encoding.ASCII.GetBytes(s);
	}
	public static string getString(this byte[] bb) {
		return Encoding.ASCII.GetString(bb);
	}
	public static string getString(this byte[] bb, int index, int count) {
		return Encoding.ASCII.GetString(bb, index, count);
	}
	public static byte[] clone(this byte[] b) {
		var bb = new byte[b.Length];
		b.CopyTo(bb, 0);
		return bb;
	}
}
