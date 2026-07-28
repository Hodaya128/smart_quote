using comviaServer.DAL;

namespace comviaServer.Model;

public class Customer
{
    public int CustomerID { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string? Address { get; set; }

    // ===== לוגיקה עסקית (לשעבר CustomerService) =====

    public static List<Customer> GetAll(DBServices dal) => dal.GetAllCustomers();

    public static Customer? GetById(DBServices dal, int id) => dal.GetCustomerById(id);

    public int Create(DBServices dal) => dal.InsertCustomer(this);

    public bool Update(DBServices dal, int id)
    {
        if (dal.GetCustomerById(id) == null) return false;
        dal.UpdateCustomer(id, this);
        return true;
    }

    public static bool Delete(DBServices dal, int id)
    {
        if (dal.GetCustomerById(id) == null) return false;
        dal.DeleteCustomer(id);
        return true;
    }
}
