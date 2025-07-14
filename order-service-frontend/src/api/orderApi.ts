import axios from 'axios';

const API_BASE_URL = process.env.NODE_ENV === 'production'
  ? process.env.REACT_APP_API_URL || ''
  : 'http://localhost:3000';
  
export interface Product {
  id: number;
  externalId: string;
  name: string;
  price: number;
  quantity: number;
  total: number;
}

export interface Order {
  id: number;
  externalId: string;
  totalValue: number;
  status: string;
  createdAt: string;
  products: Product[];
}

export interface OrdersResponse {
    items: Order[];
    totalCount: number;
}

const api = axios.create({
  baseURL: API_BASE_URL,
  headers: {
    'Content-Type': 'application/json',
  },
  withCredentials: true,
});

export const fetchOrders = async (params?: { 
    page?: number; 
    pageSize?: number 
  }): Promise<Order[]> => {
    try {
      const response = await api.get<{
        data: Array<{
          ExternalId: string;
          TotalValue: number;
          Status: string;
          CreatedAt: string;
          Products: Array<{
            Name: string;
            Quantity: number;
            UnitPrice: number;
            Total: number;
          }>;
        }>;
        totalItems: number;
      }>('/orders', {
        params: {
          page: params?.page || 1,
          pageSize: params?.pageSize || 10,
        },
      });
  
      // Mapeia os dados da API para o formato esperado
      return (response.data.data || []).map(item => ({
        id: 0, // Adicione um ID se necessário
        externalId: item.ExternalId,
        totalValue: item.TotalValue,
        status: item.Status,
        createdAt: item.CreatedAt,
        products: item.Products.map(p => ({
          id: 0, // Adicione um ID se necessário
          externalId: '',
          name: p.Name,
          price: p.UnitPrice,
          quantity: p.Quantity,
          total: p.Total
        }))
      }));
    } catch (error) {
      console.error('Erro ao buscar pedidos:', error);
      throw error;
    }
  };

export const fetchOrderById = async (id: string): Promise<Order> => {
  const response = await api.get<Order>(`/orders/${id}`);
  return response.data;
};
