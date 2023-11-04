namespace EntityComparer;

public class User
{
    public string FirstName { get; set; }
    public string LastName { get; set; }
    public int Age { get; set; }
    public Address Address { get; set; }
    public FlatMap FlatMap { get; set; }
    public List<Coll> Colls { get; set; }
}

public class UserDto
{
    public string FullName { get; set; }
    public int Age { get; set; }
    public Address Address { get; set; }
    public FlatMap FlatMap { get; set; }
    public List<Coll> Colls { get; set; }
}

public class UserEvent
{
    public string FullNameTest { get; set; }
    public int Age { get; set; }
    public AddressEvent Address { get; set; }
    public string FlatMap { get; set; }
    public List<Coll> Colls2 { get; set; }
}

public class Address
{
    public string Name { get; set; }
}

public class AddressEvent
{
    public string EventName { get; set; }
}

public class FlatMap
{
    public string FullName { get; set; }
}

public class Coll
{
    public int Id { get; set; }
    public string ValueEvent { get; set; }
}