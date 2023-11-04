using System.Text.Json;

namespace EntityComparer;

class Program
{
    static void Main()
    {
        var mapper = new EntityComparer();

        mapper.CreateMap<User, UserDto, UserEvent>(map =>
        {
            map.ForMember(dest => dest.FullNameTest, src => src.FirstName + " " + src.LastName, d => d.FullName);
            map.ForMember(dest => dest.Colls2, src => src.Colls, src => src.Colls);
            map.ForMember(dest => dest.FlatMap, src => src.FlatMap.FullName, src => src.FlatMap.FullName);
        });
        mapper.CreateMap<Address, Address, AddressEvent>(map =>
        {
            map.ForMember(dest => dest.EventName, src => src.Name, src => src.Name);
        });
        mapper.CreateMap<Coll, Coll, Coll>(map =>
        {
            map.ForMember(dest => dest.ValueEvent, src => src.ValueEvent, src => src.ValueEvent);
        });

        var source = new User
        {
            FirstName = "John1", LastName = "Doe", Age = 5,
            Address = new Address() { Name = "Bratislava" },
            Colls = new List<Coll> { new() { Id = 1, ValueEvent = "Test" } },
            FlatMap = new FlatMap() { FullName = "Flat Mapping" }
        };
        var req = new UserDto
        {
            FullName = "John Doe", Age = 7,
            Address = new Address() { Name = "Bratislava Rustaveliho" },
            Colls = new List<Coll> { new() { Id = 1, ValueEvent = "Test" }, new() { Id = 2, ValueEvent = "Abc" } },
            FlatMap = new FlatMap() { FullName = "Flat Mapping Updated" }
        };

        var destination = mapper.Map<User, UserDto, UserEvent>(source, req);

        Console.WriteLine(JsonSerializer.Serialize(destination));
    }
}