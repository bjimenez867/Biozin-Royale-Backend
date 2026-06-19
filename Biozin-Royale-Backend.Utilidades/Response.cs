namespace Biozin_Royale_Backend.Utilidades;

public class Response<T>
{

    public T ReturnValue { get; set; }
    public string strResponseMessage { get; set; } = string.Empty;
    public bool blnError { get; set; } = false;
    public string strResponseTittle { get; set; } = string.Empty;

    public void lpError(string tittle, string detail)
    {
        blnError = true;
        strResponseTittle = tittle;
        strResponseMessage = detail;
    }

}
