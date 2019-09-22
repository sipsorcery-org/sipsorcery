namespace SIPSorcery.Persistence
{
    public interface ISIPDialogueManager
    {
        void DualTransfer(string username, string callID1, string callID2);
    }
}
