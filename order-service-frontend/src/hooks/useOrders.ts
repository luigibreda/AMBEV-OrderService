import { useQuery } from '@tanstack/react-query';
import { fetchOrders } from '../api/orderApi';

export interface OrdersResponse {
  items: any[];
  totalCount: number;
  page: number;
  pageSize: number;
}

export const useOrders = (params: { page: number; pageSize: number }) => {
  return useQuery({
    queryKey: ['orders', params],
    queryFn: () => fetchOrders({
      page: params.page + 1, // Ajuste para backend que começa em 1
      pageSize: params.pageSize,
    }),
    placeholderData: (previousData) => previousData,
    retry: 1,
    select: (data) => data,
    refetchOnWindowFocus: false,
  });
};
