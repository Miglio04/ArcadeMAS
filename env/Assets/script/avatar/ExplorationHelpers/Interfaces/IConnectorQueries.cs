namespace script.avatar.ExplorationHelpers.Interfaces
{
    public interface IConnectorQueries
    {
        VerticalConnector FindConnectorForDoor(string doorName);
        bool IsConnectorDoor(string dName);
    }
}