import React from 'react';
import { 
  Table, 
  TableBody, 
  TableCell, 
  TableContainer, 
  TableHead, 
  TableRow, 
  Paper, 
  Typography, 
  Box, 
  CircularProgress,
  Chip,
  TablePagination
} from '@mui/material';
import { Order } from '../api/orderApi';

interface OrderListProps {
  orders: Order[];
  loading: boolean;
  totalCount: number;
  page: number;
  rowsPerPage: number;
  onPageChange: (event: unknown, newPage: number) => void;
  onRowsPerPageChange: (event: React.ChangeEvent<HTMLInputElement>) => void;
}

export const OrderList: React.FC<OrderListProps> = ({
  orders,
  loading,
  totalCount,
  page,
  rowsPerPage,
  onPageChange,
  onRowsPerPageChange,
}) => {
  const formatDate = (dateString: string) => {
    return new Date(dateString).toLocaleString();
  };

  const getStatusColor = (status?: string) => {
    if (!status) return 'default';
    
    switch (status.toLowerCase()) {
      case 'processed':
      case 'calculated':  
        return 'success';
      case 'failed':
        return 'error';
      default:
        return 'default';
    }
  };

  if (loading) {
    return (
      <Box display="flex" justifyContent="center" my={4}>
        <CircularProgress />
      </Box>
    );
  }

  console.log('Dados recebidos:', orders);

  if (orders.length === 0) {
    return (
      <Box my={4} textAlign="center">
        <Typography variant="h6">No orders found</Typography>
      </Box>
    );
  }

  return (
    <Paper elevation={3} sx={{ width: '100%', overflow: 'hidden' }}>
      <TableContainer sx={{ maxHeight: 600 }}>
        <Table stickyHeader aria-label="orders table">
          <TableHead>
            <TableRow>
              {/* <TableCell>Order ID</TableCell> */}
              <TableCell>External ID</TableCell>
              <TableCell>Status</TableCell>
              <TableCell>Total Value</TableCell>
              <TableCell>Products</TableCell>
              <TableCell>Created At</TableCell>
            </TableRow>
          </TableHead>
          <TableBody>
            {orders.map((order) => (
              <TableRow key={order.id} hover>
                {/* <TableCell>{order.id}</TableCell> */}
                <TableCell>{order.externalId}</TableCell>
                <TableCell>
                  <Chip 
                    label={order.status || 'N/A'} 
                    color={getStatusColor(order.status)} 
                    size="small"
                  />
                </TableCell>
                <TableCell>${order.totalValue.toFixed(2)}</TableCell>
                <TableCell>{order.products.length} items</TableCell>
                <TableCell>{formatDate(order.createdAt)}</TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      </TableContainer>
      <TablePagination
        rowsPerPageOptions={[5, 10, 25]}
        component="div"
        count={totalCount}
        rowsPerPage={rowsPerPage}
        page={page}
        onPageChange={onPageChange}
        onRowsPerPageChange={onRowsPerPageChange}
      />
    </Paper>
  );
};
