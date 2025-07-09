namespace OrderService.DTOs
{
    public class GenerateOrdersRequest
    {
        public int Count { get; set; }
        public int ProductsPerOrder { get; set; } = 100; // Default para 3 produtos por pedido
        public int DelayMs { get; set; } = 0; // Atraso em milissegundos entre cada pedido (0 para burst)
    }
}